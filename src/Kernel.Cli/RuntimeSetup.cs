using Kernel.Core;

namespace Kernel.Cli;

internal static class RuntimeSetup
{
    public static string FindRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
                if (File.Exists(Path.Combine(current.FullName, "global.json")) &&
                    File.Exists(Path.Combine(current.FullName, "tools", "z3.json")))
                    return current.FullName;
        }
        throw new InvalidOperationException("Не найдены global.json и tools/z3.json рядом с прототипом.");
    }

    public static async Task<Z3Solver> CreateSolverAsync(string root)
    {
        var config = TrustedSolverConfiguration.Load(Path.Combine(root, "tools", "z3.json"));
        if (!File.Exists(config.ExecutablePath))
            throw new InvalidOperationException("Z3 отсутствует. Выполните: pwsh -File tools/Install-Z3.ps1");
        return await Z3Solver.CreateAsync(config.ExecutablePath, config.Sha256, config.Version);
    }
}
