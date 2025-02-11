using AssistantPoc.Core.Constants;

namespace AssistantPoc.Core.Models;

public abstract class BaseResponse
{
    public ResponseStatus Status { get; set; }
    public IEnumerable<string>? ForParams { get; set; }
}
