using System;
using System.Linq;
using AtilganSak.Skills.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AtilganSak.Skills.Editing
{
    /// Puts the ready-made SkillTreePanel prefab into the scene's first Canvas (a Canvas and an EventSystem are made if
    /// the scene has none). The panel is a prefab instance: edit it in place, or Unpack it to change its structure.
    internal static class SkillTreePanelMenu
    {
        [MenuItem("GameObject/UI/Skill Tree Panel", false, 100)]
        [MenuItem("Tools/Add Skill Tree Panel To Canvas", false, 1)]
        private static void AddPanel()
        {
            var prefab = FindPrefab();
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("Skill Tree", "SkillTreePanel.prefab was not found. Is the Skill Tree package's UI folder in the project?", "OK");
                return;
            }

            var canvas = FindCanvas(out bool createdCanvas);
            var panel = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
            Undo.RegisterCreatedObjectUndo(panel, "Add Skill Tree Panel");
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            if (createdCanvas) EnsureEventSystem();
            Selection.activeGameObject = panel;
            EditorGUIUtility.PingObject(panel);
        }

        private static GameObject FindPrefab()
        {
            // Duplicates of the prefab (the user's own) are named differently; take the one this package ships.
            var paths = AssetDatabase.FindAssets("SkillTreePanel t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == "SkillTreePanel")
                .OrderBy(p => p.StartsWith("Packages/") || p.Contains("/SkillTree/") ? 0 : 1)
                .ToList();
            return paths.Count == 0 ? null : AssetDatabase.LoadAssetAtPath<GameObject>(paths[0]);
        }

        private static Canvas FindCanvas(out bool created)
        {
            created = false;
            var canvas = UnityEngine.Object
                .FindObjectsByType<Canvas>(FindObjectsInactive.Include)
                .FirstOrDefault(c => c.isRootCanvas && c.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(c));
            if (canvas != null) return canvas;

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            created = true;
            return canvas;
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            // The Input System package's module when the project has it, else the classic one.
            var inputSystem = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            go.AddComponent(inputSystem ?? typeof(StandaloneInputModule));
        }
    }
}
