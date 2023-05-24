using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using StreamMasterDomain.Common;

namespace StreamMasterInfrastructure.Persistence;

public class AppDbContextInitialiser
{
    private readonly AppDbContext _context;
    private readonly ILogger<AppDbContextInitialiser> _logger;

    public AppDbContextInitialiser(
        ILogger<AppDbContextInitialiser> logger,
        AppDbContext context
        )
    {
        _logger = logger;
        _context = context;
    }

    public async Task InitialiseAsync()
    {
        try
        {
            if (_context.Database.IsSqlite())
            {
                await _context.Database.MigrateAsync().ConfigureAwait(false);
                await _context.ResetDBAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task ResetDBAsync()
    {
        await _context.ResetDBAsync().ConfigureAwait(false);
    }

    //public async Task SeedAsync()
    //{
    //    try
    //    {
    //        TrySeed();
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "An error occurred while seeding the database.");
    //        throw;
    //    }

    //    return Task.CompletedTask;
    //}

    public void TrySeed()
    {
        FileUtil.SetupDirectories();

    }
}
