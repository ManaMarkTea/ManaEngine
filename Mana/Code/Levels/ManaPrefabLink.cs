namespace Mana.Code.Levels
{
    /// <summary>Editor provenance on an instance root. JSON is owned by the authored-content layer.</summary>
    public sealed class ManaPrefabLink
    {
        public string AssetId { get; set; } = string.Empty;
        public string AssetPath { get; set; } = string.Empty;
        public string? BaselineJson { get; set; }
        public string? AppliedJson { get; set; }
        public string OverridesJson { get; set; } = "[]";
    }
}
