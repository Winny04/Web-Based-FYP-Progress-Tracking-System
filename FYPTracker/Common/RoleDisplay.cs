namespace FYPTracker.Common
{
    /// <summary>
    /// Derives a single display role from the user's real role memberships (UserRoles) and the
    /// session ActiveRole. ActiveRole can never grant a role that is not in the membership list.
    /// </summary>
    public static class RoleDisplay
    {
        private static readonly string[] Priority = { "SuperAdmin", "Admin", "Supervisor", "Examiner", "Student" };

        public static string Resolve(IEnumerable<string?> roles, string? activeRole)
        {
            var owned = roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.Trim()).ToList();
            if (!string.IsNullOrWhiteSpace(activeRole))
            {
                var match = owned.FirstOrDefault(r => string.Equals(r, activeRole.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            foreach (var p in Priority)
            {
                var match = owned.FirstOrDefault(r => string.Equals(r, p, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return owned.FirstOrDefault() ?? "";
        }
    }
}
