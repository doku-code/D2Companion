using D2CompanionMvc.Domain;

namespace D2CompanionMvc.Diagnostics;

public static class ArchiveValidator
{
    public static ArchiveValidationResult Validate(CompanionArchive archive)
    {
        var result = new ArchiveValidationResult();

        if (archive.Totals.Accounts != archive.Accounts.Count)
        {
            result.Warnings.Add($"Totals report {archive.Totals.Accounts} accounts, but archive contains {archive.Accounts.Count} account records.");
        }

        if (archive.Totals.Items != archive.Items.Count)
        {
            result.Warnings.Add($"Totals report {archive.Totals.Items} items, but archive contains {archive.Items.Count} item records.");
        }

        var characterCount = archive.Accounts.Sum(account => account.Characters.Count);
        if (archive.Totals.Characters != characterCount)
        {
            result.Warnings.Add($"Totals report {archive.Totals.Characters} characters, but archive contains {characterCount} character records.");
        }

        foreach (var account in archive.Accounts.Where(account => string.IsNullOrWhiteSpace(account.Name)))
        {
            result.Errors.Add("Archive contains an account with no name.");
        }

        foreach (var item in archive.Items.Where(item => string.IsNullOrWhiteSpace(item.Title)))
        {
            result.Errors.Add($"Item {item.Gid} has no title.");
        }

        return result;
    }
}
