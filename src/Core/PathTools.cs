namespace STEDI.Static
{
    public static class PathTools
    {
        /// <summary>Resolves a relative path against a head directory. Returns rooted paths unchanged.</summary>
        /// <param name="headDirectory">Base directory for resolving relative paths.</param>
        /// <param name="path">Path to resolve (may be relative or rooted).</param>
        /// <returns>Fully resolved absolute path, or the original if blank.</returns>
        public static string ResolvePath(string headDirectory, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) 
                return path;

            if (Path.IsPathRooted(path))
                return path;

            return Path.GetFullPath(Path.Combine(headDirectory, path));
        }


        /// <summary>Replaces invalid filename characters and spaces with underscores.</summary>
        /// <param name="s">Raw string to sanitise for use as a filename.</param>
        /// <returns>Sanitised filename string.</returns>
        public static string ValidFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');

            return s.Replace(' ', '_');
        }

        /// <summary>Validates that a relative path resolves to an existing file under the head directory.</summary>
        /// <param name="headDirectory">Root directory for resolving relative paths.</param>
        /// <param name="relativePath">Relative file path to check.</param>
        /// <param name="contextLabel">Label for error messages (e.g. "Models[0] (GR4J)").</param>
        /// <param name="propertyName">Property name for error messages (e.g. "StartValuesJSONPath").</param>
        public static void ValidateFileExists(string headDirectory, string relativePath, string contextLabel, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidOperationException($"{contextLabel}: {propertyName} is required.");

            string fullPath = ResolvePath(headDirectory, relativePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"{contextLabel}: {propertyName} not found: {fullPath}");
        }

        /// <summary>Validates that a relative path resolves to an existing directory under the head directory.</summary>
        /// <param name="headDirectory">Root directory for resolving relative paths.</param>
        /// <param name="relativePath">Relative directory path to check.</param>
        /// <param name="contextLabel">Label for error messages.</param>
        /// <param name="propertyName">Property name for error messages.</param>
        public static void ValidateDirectoryExists(string headDirectory, string relativePath, string contextLabel, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidOperationException($"{contextLabel}: {propertyName} is required.");

            string fullPath = ResolvePath(headDirectory, relativePath);
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException($"{contextLabel}: {propertyName} not found: {fullPath}");
        }
    }
}
