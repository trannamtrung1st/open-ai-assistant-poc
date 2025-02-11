using AssistantPoc.Core.Constants;
using AssistantPoc.Core.Models;

namespace AssistantPoc.Core;

public static class DataStore
{
    public static List<AssetEntity> Assets { get; private set; } = [];
    public static List<DeviceEntity> Devices { get; private set; } = [];
    public static List<SubscriptionEntity> Subscriptions { get; private set; } = [];
    public static List<ProjectEntity> Projects { get; private set; } = [];

    static DataStore()
    {
        Initialize();
    }

    public static void Initialize()
    {
        Assets = [];
        Assets.Add(new AssetEntity
        {
            Id = Guid.NewGuid(),
            Name = "Pump 001"
        });
        Assets.Add(new AssetEntity
        {
            Id = Guid.NewGuid(),
            Name = "Boiler 002"
        });
        Assets.Add(new AssetEntity
        {
            Id = Guid.NewGuid(),
            Name = "Palletizer 100"
        });

        Devices = [];
        Devices.Add(new DeviceEntity
        {
            Id = "device-001",
            Name = "Device 001",
            Status = DeviceConstants.Statuses.Connected
        });
        Devices.Add(new DeviceEntity
        {
            Id = "device-002",
            Name = "Device 002",
            Status = DeviceConstants.Statuses.Disconnected
        });
        Devices.Add(new DeviceEntity
        {
            Id = "device-003",
            Name = "Device 003",
            Status = DeviceConstants.Statuses.Unknown
        });

        Subscriptions = [];
        Subscriptions.Add(new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            Name = "Subscription 001"
        });
        Subscriptions.Add(new SubscriptionEntity
        {
            Id = Guid.NewGuid(),
            Name = "Subscription 002"
        });

        Projects = [];
        Projects.Add(new ProjectEntity
        {
            Id = Guid.NewGuid(),
            Name = "Project 001",
            SubscriptionId = Subscriptions[0].Id
        });
        Projects.Add(new ProjectEntity
        {
            Id = Guid.NewGuid(),
            Name = "Project 002",
            SubscriptionId = Subscriptions[1].Id
        });
    }
}
