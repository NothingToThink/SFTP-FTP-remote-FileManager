using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Manages connection instances and connection state operations.
/// </summary>
[ApiController]
[Route("connections")]
[Produces("application/json")]
[Tags("Connections")]
public class ConnectionsController(
    ILogger<ConnectionsController> logger,
    IConnectionManager connectionManager
) : ControllerBase
{
    /// <summary>
    /// Retrieves a list of all active connection identifiers.
    /// </summary>
    /// <returns>A collection of connection GUIDs.</returns>
    /// <response code="200">List of connection IDs retrieved successfully.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Guid>), StatusCodes.Status200OK)]
    public IActionResult GetConnectionIdList()
    {
        logger.LogInformation("Getting connection id list.");
        var connectionIdList = connectionManager.GetConnectionIdList();
        return Ok(connectionIdList);
    }

    /// <summary>
    /// Creates a new connection instance using the provided profile configuration.
    /// </summary>
    /// <param name="profile">The profile used to initialize the connection.</param>
    /// <returns>The identifier assigned to the new connection instance.</returns>
    /// <response code="200">Connection created successfully.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    public IActionResult CreateConnection([FromBody] SavedProfile profile)
    {
        logger.LogInformation("Creating connection.");
        return Ok(connectionManager.CreateConnection(profile));
    }

    /// <summary>
    /// Deletes a connection instance by its ID.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the connection instance.</param>
    /// <response code="200">Connection instance deleted successfully.</response>
    [HttpDelete("{connectionId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DeleteConnection([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Deleting connection.");
        connectionManager.DeleteConnection(connectionId);
        return Ok();
    }

    /// <summary>
    /// Establishes the connection to the remote server.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the connection instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Connected successfully.</response>
    [HttpPost("{connectionId}/connect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConnectConnection([FromRoute] Guid connectionId, CancellationToken ct)
    {
        logger.LogInformation("Connecting connection.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.ConnectAsync(ct);
        return Ok();
    }

    /// <summary>
    /// Disconnects the active connection from the remote server.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the connection instance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Disconnected successfully.</response>
    [HttpPost("{connectionId}/disconnect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DisconnectConnection([FromRoute] Guid connectionId, CancellationToken ct)
    {
        logger.LogInformation("Disconnecting connection.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.DisconnectAsync(ct);
        return Ok();

    }

    /// <summary>
    /// Retrieves the current connection state.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the connection instance.</param>
    /// <returns><c>true</c> if connected; otherwise, <c>false</c>.</returns>
    /// <response code="200">Returns current connection state.</response>
    [HttpGet("{connectionId}/state")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public IActionResult GetConnectionState([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Getting connection state.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(connection.IsConnected);
    }
}
