using System;
using System.IO;

namespace MSAVA_API.Tests;

public class ApiPipelineConfigurationTests
{
    [Test]
    public void Program_UsesHstsOutsideDevelopmentBeforeHttpsRedirection()
    {
        string programText = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, "MSAVA-API", "Program.cs"));
        string normalizedProgramText = programText.Replace("\r\n", "\n");

        normalizedProgramText.Should().Contain("else\n{\n    app.UseHsts();\n}");

        int developmentBranchIndex = normalizedProgramText.IndexOf("if (app.Environment.IsDevelopment())", StringComparison.Ordinal);
        int hstsIndex = normalizedProgramText.IndexOf("app.UseHsts();", StringComparison.Ordinal);
        int httpsRedirectionIndex = normalizedProgramText.IndexOf("app.UseHttpsRedirection();", StringComparison.Ordinal);

        developmentBranchIndex.Should().BeGreaterThanOrEqualTo(0);
        hstsIndex.Should().BeGreaterThan(developmentBranchIndex);
        httpsRedirectionIndex.Should().BeGreaterThan(hstsIndex);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MSAVA-API", "Program.cs")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MSAVA repository root.");
    }
}
