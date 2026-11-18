namespace HeatInteractive.SkillTreeGenerator
{
    /// One validation finding. Node is null for tree-wide ones.
    public readonly struct SkillIssue
    {
        public readonly SkillNode Node;
        public readonly string Message;
        public readonly bool IsError;

        public SkillIssue(SkillNode node, string message, bool isError)
        {
            Node = node;
            Message = message;
            IsError = isError;
        }

        public static SkillIssue Error(SkillNode node, string message) => new(node, message, true);
        public static SkillIssue Warning(SkillNode node, string message) => new(node, message, false);
    }
}
