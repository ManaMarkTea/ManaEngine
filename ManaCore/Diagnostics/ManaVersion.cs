using System.Reflection;

namespace ManaCore.Diagnostics
{
    /// <summary>
    /// The app's version, from the assembly attributes every project gets from Directory.Build.props: the number,
    /// the git commit it was built from (the SDK adds it to the informational version) and the build date.
    /// </summary>
    public static class ManaVersion
    {
        private static readonly Assembly Assembly = typeof(ManaVersion).Assembly;

        private static readonly string Informational =
            Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

        /// <summary>The release number, e.g. "0.1.0".</summary>
        public static string Number { get; } = Informational.Split('+')[0];

        /// <summary>The commit, shortened, or empty when the build was not made from a git checkout.</summary>
        public static string Commit { get; } = ShortCommit(Informational);

        /// <summary>The UTC date the build was made, or empty.</summary>
        public static string BuildDate { get; } = Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildDate")?.Value ?? string.Empty;

        /// <summary>"v0.1.0 (1a2b3c4)", for the status bar.</summary>
        public static string Display { get; } = Commit.Length == 0 ? $"v{Number}" : $"v{Number} ({Commit})";

        private static string ShortCommit(string informational)
        {
            var plus = informational.IndexOf('+');
            if (plus < 0)
            {
                return string.Empty;
            }

            var commit = informational[(plus + 1)..];
            return commit.Length > 7 ? commit[..7] : commit;
        }
    }
}
