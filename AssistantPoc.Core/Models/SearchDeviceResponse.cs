namespace AssistantPoc.Core.Models;

public class SearchDeviceResponse : BaseResponse
{
    public IEnumerable<DeviceEntity>? Data { get; set; }
}