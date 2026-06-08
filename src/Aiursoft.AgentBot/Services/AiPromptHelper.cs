namespace Aiursoft.AgentBot.Services;

/// <summary>
/// Provides common AI prompt additions for coding guidelines that apply across
/// different bot workflows (issue resolution, MR fixes, etc.).
/// </summary>
public static class AiPromptHelper
{
    /// <summary>
    /// Returns Entity Framework migration guidelines to be appended to AI prompts.
    /// Ensures the AI uses the EF CLI tooling instead of manually writing migration files.
    /// </summary>
    public static string GetEfMigrationGuidelines()
    {
        return @"

## ⚠️ CRITICAL: Entity Framework Migration Rules ⚠️

If this project uses Entity Framework Core (check for Microsoft.EntityFrameworkCore in .csproj or packages.config files), you MUST follow these strict rules:

1. **Confirm Project Type**: Check that the project uses EF Core Code First approach (look for DbContext-derived classes and an existing Migrations folder with generated migration files).

2. **Generate Migrations via CLI — NEVER manually write them**:
   - Use: `dotnet ef migrations add <DescriptiveMigrationName>`
   - Example: `dotnet ef migrations add AddUserEmailColumn`
   - The EF tooling will auto-generate the migration C# files for you.

3. **STRICTLY FORBIDDEN**:
   - ❌ Do NOT manually create any .cs file in the Migrations/ folder.
   - ❌ Do NOT manually edit auto-generated migration files.
   - ❌ Do NOT write migration code (Up/Down methods) by hand.
   - ✅ ALWAYS use `dotnet ef migrations add` to scaffold migrations.

4. **Apply migrations** (when appropriate):
   - Use: `dotnet ef database update` to apply pending migrations to the database.

5. **Model changes before migration**:
   - Make your entity/model class changes first (e.g., add properties to DbContext or entity classes).
   - Then generate the migration via CLI — it will detect model changes automatically.";
    }
}
