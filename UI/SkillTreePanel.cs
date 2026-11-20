using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace HeatInteractive.SkillTreeGenerator.UI
{
    public enum SkillReveal
    {
        /// Every node is drawn from the start.
        Everything,
        /// Only what the player owns and what can be bought next; the rest stays out of sight until it is reachable.
        OwnedAndReachable
    }

    /// The ready-made skill tree screen. Drop the SkillTreePanel prefab in a Canvas (Tools > Add Skill Tree Panel To
    /// Canvas) and it shows SkillBook.Current's tree: nodes and links drawn from the tree asset, drag to pan, wheel to
    /// zoom, click a node to read it, Buy to rank it up, Respec to refund. Open and close it by enabling the object.
    ///
    /// Every piece is a prefab or a child you can restyle: the node (SkillNodeView), the link (SkillLinkView), the info
    /// card (SkillDetailView), the currency rows (SkillCostRow). Duplicate one and assign your copy here. Slots left empty
    /// are skipped. Transition hooks: UnityEvents on the node / link / detail prefabs, SkillNodeFeedback components in the
    /// node prefab, and the C# events below.
    ///
    /// The panel redraws when the book changes and when the wallet's balances change. If the game changes other things the
    /// panel shows (a price stat...), call Refresh().
    public class SkillTreePanel : MonoBehaviour
    {
        [Header("Tree")]
        public SkillTreeViewport Viewport;
        [Tooltip("Links are made under this one (drawn first, so under the nodes).")]
        public RectTransform LinkRoot;
        public RectTransform NodeRoot;
        public SkillNodeView NodePrefab;
        [Tooltip("Optional: a different prefab for the root node. Empty = the node prefab, scaled by Root Scale.")]
        public SkillNodeView RootNodePrefab;
        public SkillLinkView LinkPrefab;
        [Min(0.1f)] public float RootScale = 1.4f;
        [Min(0.05f)] public float StartZoom = 0.55f;
        public SkillReveal Reveal = SkillReveal.OwnedAndReachable;

        [Header("Parts (all optional)")]
        public SkillDetailView Detail;
        [Tooltip("Currency rows go here (every currency the tree's prices use).")]
        public RectTransform WalletRoot;
        public SkillCostRow WalletRowPrefab;
        [Tooltip("Only shown while Allow Respec is on.")]
        public Button RespecButton;
        public Button CloseButton;
        [Tooltip("Shown while there is no tree to draw.")]
        public GameObject EmptyState;

        [Header("Respec")]
        [Tooltip("Off (default): the Respec button is hidden, skills can't be refunded from the panel.")]
        public bool AllowRespec = false;
        [Tooltip("Asked before a respec: Respec opens it with Yes / Cancel. Empty = respec right away.")]
        public SkillConfirmPopup ConfirmPopup;

        [Header("Close")]
        public bool DeactivateOnClose = true;

        [Header("Hooks")]
        public UnityEvent Opened;
        public UnityEvent Closed;
        public UnityEvent Respecced;

        [Header("Editor preview")]
        [Tooltip("Right-click the component > Preview Tree: draws this tree in the edit-mode scene, so you can style the prefabs. Not saved.")]
        public SkillTree PreviewTree;

        /// Set from code to veto a state (e.g. a planned skill that can't be bought yet always shows Locked). Gets the
        /// node and the state StateOf worked out, returns the state to show.
        public Func<SkillNode, SkillNodeState, SkillNodeState> StateFilter;
        /// Set from code to hide some currency rows. Gets the currency, returns whether to show it.
        public Func<SkillCurrency, bool> CurrencyFilter;

        public event Action<SkillNodeView> NodeSelected;
        public event Action<SkillNodeView> NodePurchased;
        public event Action<SkillNodeView, string> BuyFailed;
        public event Action Refreshed;

        private SkillBook _bound, _subscribed;
        private SkillTree _builtTree;
        private readonly List<SkillNodeView> _nodes = new();
        private readonly Dictionary<SkillNode, SkillNodeView> _views = new(); // node -> view, so links and Select don't scan _nodes
        private readonly List<SkillLinkView> _links = new();
        private readonly List<(SkillCurrency currency, SkillCostRow row)> _wallet = new();
        private SkillNodeView _selected;
        private int _walletSignature;
        private bool _dirty;

        public IReadOnlyList<SkillNodeView> Nodes => _nodes;
        public SkillNodeView Selected => _selected;

        /// The player's progress: Bind(book) if the game gave one, else SkillBook.Current.
        public SkillBook Book => _bound ?? SkillBook.Current;

        /// Show this book instead of SkillBook.Current (a second player, a preview). Null goes back to Current.
        public void Bind(SkillBook book)
        {
            _bound = book;
            if (isActiveAndEnabled) Rebind();
        }

        private void OnEnable()
        {
            if (CloseButton != null) CloseButton.onClick.AddListener(Close);
            if (RespecButton != null) RespecButton.onClick.AddListener(Respec);
            if (Detail != null && Detail.BuyButton != null) Detail.BuyButton.onClick.AddListener(OnBuyClicked);
            Rebind();
            ApplyRespecVisibility();
            if (Application.isPlaying) Opened?.Invoke();
        }

        private void OnDisable()
        {
            if (CloseButton != null) CloseButton.onClick.RemoveListener(Close);
            if (RespecButton != null) RespecButton.onClick.RemoveListener(Respec);
            if (Detail != null && Detail.BuyButton != null) Detail.BuyButton.onClick.RemoveListener(OnBuyClicked);
            if (_subscribed != null) _subscribed.RankChanged -= OnRankChanged;
            _subscribed = null;
            if (ConfirmPopup != null) ConfirmPopup.gameObject.SetActive(false); // a popup must not outlive the panel's open
        }

        private void Rebind()
        {
            if (_subscribed != null) _subscribed.RankChanged -= OnRankChanged;
            _subscribed = Book;
            _subscribed.RankChanged += OnRankChanged;
            Rebuild();
        }

        // A respec raises one per owned node, so repaint once (Update, or right after Buy / ConfirmRespec), not per event.
        private void OnRankChanged(SkillNode node, int from, int to) => _dirty = true;

        private void Update()
        {
            // The game sets SkillBook.Current once its save is loaded, maybe after this panel opened.
            if (Book != _subscribed)
            {
                Rebind();
                return;
            }
            if (_nodes.Count == 0) return;

            // The book only says when ranks change; money moves on its own, so watch the balances (no allocations).
            int signature = WalletSignature();
            if (_dirty || signature != _walletSignature) Refresh();
        }

        private int WalletSignature()
        {
            var wallet = Book.Wallet;
            if (wallet == null) return 0;
            int hash = 17;
            foreach (var (currency, _) in _wallet) hash = hash * 31 + wallet.Balance(currency);
            return hash;
        }

        // ---------------------------------------------------------------- build

        /// Draws everything again from the book's tree. Done on enable; call it after swapping the tree's contents.
        public void Rebuild()
        {
            Clear(LinkRoot);
            Clear(NodeRoot);
            _nodes.Clear();
            _views.Clear();
            _links.Clear();
            _wallet.Clear();
            if (WalletRoot != null) Clear(WalletRoot);
            _selected = null;

            var tree = Book.Tree;
            _builtTree = tree;
            bool any = tree != null && tree.Nodes.Exists(n => n != null);
            if (EmptyState != null) EmptyState.SetActive(!any);
            if (!any)
            {
                if (Detail != null) Detail.Clear();
                return;
            }

            tree.Invalidate();
            BuildLinks(tree);
            foreach (var node in tree.Nodes)
                if (node != null) AddNode(node);
            BuildWallet(tree);
            if (_nodes.Count == 0) // no node prefab or root: nothing to show
            {
                if (EmptyState != null) EmptyState.SetActive(true);
                return;
            }

            if (Viewport != null)
            {
                Viewport.ResetView(StartZoom);
                if (tree.Root != null) Viewport.Focus(tree.Root.Position);
            }
            var start = _nodes.Find(v => v.Node.IsRoot) ?? _nodes[0];
            _selected = start;
            Refresh(true);
            start.SetSelected(true, true);
            ShowDetail();
        }

        private void BuildLinks(SkillTree tree)
        {
            if (LinkPrefab == null || LinkRoot == null) return;
            var seen = new HashSet<long>();
            foreach (var node in tree.Nodes)
            {
                if (node == null) continue;
                foreach (var other in tree.LinksOf(node))
                {
                    long key = ((long)Mathf.Min(node.Id, other.Id) << 32) | (uint)Mathf.Max(node.Id, other.Id);
                    if (!seen.Add(key)) continue;
                    var link = Instantiate(LinkPrefab, LinkRoot);
                    link.name = $"{node.name} - {other.name}";
                    CenterAnchors((RectTransform)link.transform);
                    link.Place(node, other);
                    link.Apply(tree.LinkStyleOf(node, other));
                    _links.Add(link);
                }
            }
        }

        private void AddNode(SkillNode node)
        {
            var prefab = PrefabFor(node);
            if (prefab == null || NodeRoot == null) return;
            var view = Instantiate(prefab, NodeRoot);
            view.name = node.name;
            var rect = (RectTransform)view.transform;
            CenterAnchors(rect);
            rect.anchoredPosition = node.Position;
            if (node.IsRoot && RootNodePrefab == null && prefab == NodePrefab) rect.localScale = Vector3.one * RootScale; // a custom view keeps its own size
            view.Bind(node);
            view.Clicked = Select;
            _nodes.Add(view);
            _views[node] = view;
        }

        /// Which prefab draws this node: the node's own ViewPrefab if it has one, else the root / node prefab. Override to
        /// pick by node type or group.
        protected virtual SkillNodeView PrefabFor(SkillNode node)
        {
            if (node.ViewPrefab != null)
            {
                if (node.ViewPrefab.TryGetComponent<SkillNodeView>(out var custom)) return custom;
                Debug.LogWarning($"Skills: \"{node.DisplayName}\" has a View Prefab without a SkillNodeView on its root, so the default view is used.", node);
            }
            return node.IsRoot && RootNodePrefab != null ? RootNodePrefab : NodePrefab;
        }

        private void BuildWallet(SkillTree tree)
        {
            if (WalletRoot == null || WalletRowPrefab == null) return;
            var seen = new HashSet<SkillCurrency>();
            foreach (var node in tree.Nodes)
            {
                if (node == null || node.Cost == null) continue;
                foreach (var cost in node.Cost)
                    if (cost.Currency != null && seen.Add(cost.Currency))
                        _wallet.Add((cost.Currency, Instantiate(WalletRowPrefab, WalletRoot)));
            }
        }

        private static void CenterAnchors(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        }

        private static void Clear(RectTransform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    child.SetActive(false);
                    Destroy(child);
                }
                else DestroyImmediate(child);
            }
        }

        // ---------------------------------------------------------------- refresh

        /// Repaints every node, link, the wallet and the info card from the book. Cheap; no hooks fire for what didn't change.
        public void Refresh() => Refresh(false);

        private void Refresh(bool instant)
        {
            _dirty = false;
            var book = Book;
            if (book.Tree != _builtTree)
            {
                Rebuild();
                return;
            }

            foreach (var view in _nodes) view.Apply(StateOf(book, view.Node), book.GetRank(view.Node), instant);
            foreach (var link in _links)
            {
                bool shown = Shows(link.A) && Shows(link.B);
                link.SetVisible(shown);
                if (shown) link.SetOn(book.IsOwned(link.A) && book.IsOwned(link.B), instant);
            }

            // A respec can hide the picked one: back to the root, which always shows.
            if (_selected != null && _selected.State == SkillNodeState.Hidden)
            {
                _selected.SetSelected(false, true);
                _selected = _nodes.Find(v => v.Node.IsRoot) ?? _nodes[0];
                _selected.SetSelected(true, instant);
            }

            RefreshWallet(book);
            ApplyRespecVisibility();
            ShowDetail();
            Refreshed?.Invoke();
        }

        private bool Shows(SkillNode node) => _views.TryGetValue(node, out var v) && v.State != SkillNodeState.Hidden;

        /// What a node looks like to the player. Override for your own rules.
        protected virtual SkillNodeState StateOf(SkillBook book, SkillNode node)
        {
            var state = BaseStateOf(book, node);
            return StateFilter != null ? StateFilter(node, state) : state;
        }

        private SkillNodeState BaseStateOf(SkillBook book, SkillNode node)
        {
            int rank = book.GetRank(node);
            bool reachable = book.IsReachable(node);
            if (Reveal == SkillReveal.OwnedAndReachable && rank == 0 && !reachable) return SkillNodeState.Hidden;
            if (rank >= node.MaxRank) return SkillNodeState.Maxed;
            if (rank > 0) return SkillNodeState.Owned;
            if (!book.IsBuyable(node)) return SkillNodeState.Locked;
            return book.CanBuy(node) ? SkillNodeState.Affordable : SkillNodeState.Available;
        }

        /// Which currency rows show in the wallet. Override to hide some (a currency not found yet...).
        protected virtual bool ShowCurrency(SkillCurrency currency) => CurrencyFilter == null || CurrencyFilter(currency);

        private void RefreshWallet(SkillBook book)
        {
            if (WalletRoot != null) WalletRoot.gameObject.SetActive(book.Wallet != null);
            _walletSignature = WalletSignature();
            if (book.Wallet == null) return;
            foreach (var (currency, row) in _wallet)
            {
                bool show = ShowCurrency(currency);
                row.gameObject.SetActive(show);
                if (show) row.Bind(currency, book.Wallet.Balance(currency).ToString());
            }
        }

        private void ShowDetail()
        {
            if (Detail == null) return;
            if (_selected == null) Detail.Clear();
            else Detail.Show(_selected.Node, Book);
        }

        // ---------------------------------------------------------------- actions (wire your own buttons to these)

        public void Select(SkillNodeView view)
        {
            if (view == null || view == _selected) return;
            if (_selected != null) _selected.SetSelected(false);
            _selected = view;
            view.SetSelected(true);
            ShowDetail();
            NodeSelected?.Invoke(view);
        }

        public void Select(SkillNode node) => Select(node != null && _views.TryGetValue(node, out var v) ? v : null);

        private void OnBuyClicked() => Buy();

        /// Buys a rank of the picked node. False with a hook (BuyFailed) when it can't.
        public bool Buy()
        {
            if (_selected == null) return false;
            var view = _selected;
            string why = Book.WhyNot(view.Node);
            if (why != null)
            {
                view.NotifyBuyFailed(why);
                BuyFailed?.Invoke(view, why);
                return false;
            }
            if (!Book.TryBuy(view.Node)) return false;
            if (_dirty) Refresh(); // so NodePurchased handlers see the new state
            NodePurchased?.Invoke(view);
            return true;
        }

        /// Refunds everything, after the confirm popup's Yes (no popup assigned: right away).
        public void Respec()
        {
            if (ConfirmPopup != null) ConfirmPopup.Open(ConfirmRespec);
            else ConfirmRespec();
        }

        /// Refunds everything now, no question asked.
        public void ConfirmRespec()
        {
            Book.Respec();
            if (_dirty) Refresh();
            Respecced?.Invoke();
        }

        private void ApplyRespecVisibility()
        {
            if (RespecButton != null && RespecButton.gameObject.activeSelf != AllowRespec) RespecButton.gameObject.SetActive(AllowRespec);
        }

        public void Close()
        {
            Closed?.Invoke();
            if (DeactivateOnClose) gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- editor preview

        [ContextMenu("Preview Tree")]
        private void PreviewInEditor()
        {
            if (Application.isPlaying || PreviewTree == null) return;
            var book = new SkillBook(PreviewTree, null, null);
            if (PreviewTree.Root != null) book.SetRank(PreviewTree.Root, 1);
            _bound = book;
            Rebuild();
            foreach (var child in new[] { LinkRoot, NodeRoot, WalletRoot })
                if (child != null)
                    foreach (Transform t in child)
                        t.gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        [ContextMenu("Clear Preview")]
        private void ClearPreview()
        {
            if (Application.isPlaying) return;
            _bound = null;
            Clear(LinkRoot);
            Clear(NodeRoot);
            Clear(WalletRoot);
            _nodes.Clear();
            _views.Clear();
            _links.Clear();
            _wallet.Clear();
            _builtTree = null;
        }
    }
}
