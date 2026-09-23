using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DistrictsController : ControllerBase
{
    /// <summary>
    /// The 25 districts of Sri Lanka, for the picker in the app's notification
    /// settings.
    ///
    /// Anonymous: the list is public knowledge, and the app needs it on the
    /// registration path before anyone holds a token.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> List() => Ok(SriLankaDistricts.All);
}
