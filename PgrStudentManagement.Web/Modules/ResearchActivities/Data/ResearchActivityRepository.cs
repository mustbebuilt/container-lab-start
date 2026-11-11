using MongoDB.Driver;
using PgrStudentManagement.Web.Modules.ResearchActivities.Models;

namespace PgrStudentManagement.Web.Modules.ResearchActivities.Data;

public class ResearchActivityRepository
{
    private const string DefaultConnectionString = "mongodb://localhost:27017";
    private const string DefaultDatabaseName = "StudentManagement";
    private const string DefaultCollectionName = "ResearchActivities";

    private readonly IMongoCollection<ResearchActivity> _collection;

    public ResearchActivityRepository(IMongoClient mongoClient, IConfiguration configuration)
    {
        var databaseName = configuration["ResearchActivityDatabase:DatabaseName"] ?? DefaultDatabaseName;
        var collectionName = configuration["ResearchActivityDatabase:CollectionName"] ?? DefaultCollectionName;

        var database = mongoClient.GetDatabase(databaseName);
        _collection = database.GetCollection<ResearchActivity>(collectionName);
    }

    public ResearchActivityRepository(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDb")
            ?? configuration["ResearchActivityDatabase:ConnectionString"]
            ?? DefaultConnectionString;
        var databaseName = configuration["ResearchActivityDatabase:DatabaseName"] ?? DefaultDatabaseName;
        var collectionName = configuration["ResearchActivityDatabase:CollectionName"] ?? DefaultCollectionName;

        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        _collection = database.GetCollection<ResearchActivity>(collectionName);
    }

    public async Task<IReadOnlyList<ResearchActivity>> GetAllAsync()
    {
        var cursor = await _collection.FindAsync(_ => true);
        var activities = await cursor.ToListAsync();
        return activities.OrderBy(activity => activity.ActivityDate).ToList();
    }

    public async Task<IReadOnlyList<ResearchActivity>> GetForStudentAsync(string studentNumber)
    {
        var filter = Builders<ResearchActivity>.Filter.Eq(a => a.StudentNumber, studentNumber);
        var cursor = await _collection.FindAsync(filter);
        var activities = await cursor.ToListAsync();
        return activities.OrderBy(activity => activity.ActivityDate).ToList();
    }

    public async Task AddAsync(ResearchActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        // Check if an activity with this Id already exists to avoid duplicate key exceptions during seeding
        var filter = Builders<ResearchActivity>.Filter.Eq(a => a.Id, activity.Id);
        var existing = await _collection.Find(filter).FirstOrDefaultAsync();
        if (existing is null)
        {
            await _collection.InsertOneAsync(activity);
        }
    }
}
