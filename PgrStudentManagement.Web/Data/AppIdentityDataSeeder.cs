using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace PgrStudentManagement.Web.Data;

public class AppIdentityDataSeeder
{
    private readonly AppIdentityDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<AppIdentityDataSeeder> _logger;

    public AppIdentityDataSeeder(
        AppIdentityDbContext context,
        UserManager<IdentityUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<AppIdentityDataSeeder> logger)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        try
        {
            await _context.Database.EnsureCreatedAsync();

            string[] roles = ["SupervisorAdmin", "Supervisor"];
            foreach (var role in roles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    await _roleManager.CreateAsync(new IdentityRole(role));
                    _logger.LogInformation("Seeded Identity role: {Role}", role);
                }
            }

            const string defaultEmail = "supervisor@pgr.ac.uk";
            var existingUser = await _userManager.FindByEmailAsync(defaultEmail);
            if (existingUser is null)
            {
                var user = new IdentityUser
                {
                    UserName = defaultEmail,
                    Email = defaultEmail,
                    EmailConfirmed = true
                };

                var result = await _userManager.CreateAsync(user, "Password123!");
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(user, "Supervisor");
                    _logger.LogInformation("Seeded default Identity user: {Email} (Password: Password123!)", defaultEmail);
                }
                else
                {
                    _logger.LogWarning("Failed to seed default user: {Errors}",
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding Identity database in PostgreSQL.");
            throw;
        }
    }
}

