using AssistantPoc.Core.Models;

namespace AssistantPoc.Core;

public static class DataStore
{
    public static List<AssetEntity> Assets { get; private set; } = [];

    static DataStore()
    {
        Initialize();
    }

    public static void Initialize()
    {
        Assets = [];
        Assets.Add(new AssetEntity
        {
            Id = Guid.Parse("2a8ebca1-3cba-4fd1-937b-ba933af12fb2"),
            Name = "Pump 001"
        });
        Assets.Add(new AssetEntity
        {
            Id = Guid.Parse("a10c9b73-3dd9-40dc-8eaf-08c2c078ec80"),
            Name = "Boiler 002"
        });
        Assets.Add(new AssetEntity
        {
            Id = Guid.Parse("a55e47bd-b286-48f7-8301-f91c580b19bc"),
            Name = "Palletizer 100"
        });
    }
}
