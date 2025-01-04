using Microsoft.AspNetCore.Mvc;
using AssistantPoc.Core.Models;
using AssistantPoc.Core;

namespace AssistantPoc.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AssetsController : ControllerBase
{
    [HttpGet]
    public ActionResult<IEnumerable<AssetEntity>> GetAssets()
    {
        return Ok(DataStore.Assets);
    }

    [HttpGet("{id}")]
    public ActionResult<AssetEntity> GetAssetById(Guid id)
    {
        var asset = DataStore.Assets.FirstOrDefault(a => a.Id == id);
        if (asset == null)
        {
            return NotFound();
        }
        return Ok(asset);
    }
} 