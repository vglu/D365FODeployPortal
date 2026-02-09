using System.Diagnostics;

namespace DeployPortal.PackageOps;

/// <summary>
/// LCS → Unified conversion using external ModelUtil.exe.
/// Does NOT support Unified → LCS (use ConvertEngine for that).
/// </summary>
public class ModelUtilConvertEngine
{
    private readonly string _modelUtilPath;

    public ModelUtilConvertEngine(string modelUtilPath)
    {
        if (string.IsNullOrWhiteSpace(modelUtilPath))
            throw new ArgumentException("ModelUtil.exe path must be specified.", nameof(modelUtilPath));
        _modelUtilPath = modelUtilPath;
    }

    /// <summary>
    /// Converts an LCS package ZIP to Unified format using ModelUtil.exe.
    /// Returns the path to the output directory containing TemplatePackage.dll.
    /// </summary>
    public async Task<string> ConvertToUnifiedAsync(string lcsPackagePath, Action<string>? onLog = null)
    {
        var outputDir = Path.Combine(
            Path.GetDirectoryName(lcsPackagePath)!,
            Path.GetFileNameWithoutExtension(lcsPackagePath) + "_unified");

        Directory.CreateDirectory(outputDir);

        onLog?.Invoke("[ModelUtil] Converting LCS → Unified...");
        onLog?.Invoke($"  Input:  {lcsPackagePath}");
        onLog?.Invoke($"  Output: {outputDir}");

        var psi = new ProcessStartInfo
        {
            FileName = _modelUtilPath,
            Arguments = $"-convertToUnifiedPackage -file=\"{lcsPackagePath}\" -outputpath=\"{outputDir}\"",
            WorkingDirectory = Path.GetDirectoryName(_modelUtilPath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = ReadStreamAsync(process.StandardOutput, line =>
        {
            onLog?.Invoke(line);
        });
        var errorTask = ReadStreamAsync(process.StandardError, line =>
        {
            onLog?.Invoke($"[ERROR] {line}");
        });

        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);

        if (process.ExitCode != 0)
            throw new Exception($"ModelUtil.exe failed with exit code {process.ExitCode}");

        var templateDll = Path.Combine(outputDir, "TemplatePackage.dll");
        if (!File.Exists(templateDll))
            throw new FileNotFoundException("TemplatePackage.dll not found after conversion", templateDll);

        onLog?.Invoke("[ModelUtil] Conversion completed successfully.");
        return outputDir;
    }

    private static async Task ReadStreamAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                onLine(line);
        }
    }
}
