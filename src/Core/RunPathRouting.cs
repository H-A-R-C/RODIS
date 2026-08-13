namespace RODIS.Static
{
    public static class RunPathRouting
    {
        public sealed class RoutedPaths
        {
            public string HeadDirectory { get; init; } = string.Empty;
            public string OutFolder { get; init; } = string.Empty;

            public Dictionary<string, string> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Outputs { get; } = new(StringComparer.OrdinalIgnoreCase);

            public string GetInput(string key) => Inputs[key];
            public string GetOutput(string key) => Outputs[key];
        }

        public static RoutedPaths Resolve(
            string headDirectory,
            string outFolderRaw,
            IReadOnlyDictionary<string, string> inputPaths,
            IReadOnlyDictionary<string, string> outputPaths,
            Action<string>? log = null)
        {
            if (string.IsNullOrWhiteSpace(headDirectory))
                throw new InvalidOperationException("HeadDirectory is missing.");

            string ResolveInput(string raw) =>
                PathTools.ResolvePath(headDirectory, raw);

            string resolvedOut = PathTools.ResolvePath(headDirectory, outFolderRaw);
            Directory.CreateDirectory(resolvedOut);

            log?.Invoke($"OutFolder = {resolvedOut}");

            string ResolveOutput(string raw) =>
                Path.IsPathRooted(raw)
                    ? Path.GetFullPath(raw)
                    : Path.GetFullPath(Path.Combine(resolvedOut, raw));

            var result = new RoutedPaths
            {
                HeadDirectory = headDirectory,
                OutFolder = resolvedOut
            };

            // Inputs: must exist
            foreach (var (key, raw) in inputPaths)
            {
                string path = ResolveInput(raw);
                if (!File.Exists(path) && !Directory.Exists(path))
                    throw new InvalidOperationException($"Input '{key}' does not exist: {path}");

                result.Inputs[key] = path;
                log?.Invoke($"{key} = {path}");
            }

            // Outputs: create parent directories
            foreach (var (key, raw) in outputPaths)
            {
                string path = ResolveOutput(raw);
                string? parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);

                result.Outputs[key] = path;
                log?.Invoke($"{key} = {path}");
            }

            return result;
        }
    }
}
