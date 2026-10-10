using Core.Interfaces.Manager;
using Core.Models.Credentials;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Manages active connection instances and connection lifecycle.
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
    /// <returns>A collection of active connection GUIDs.</returns>
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
    /// Initializes a new connection instance using the provided profile configuration.
    /// </summary>
    /// <param name="profile">The profile used to establish the connection configuration.</param>
    /// <returns>The unique identifier assigned to the created connection instance.</returns>
    /// <response code="200">Connection instance created successfully.</response>
    /// <response code="400">Invalid profile configuration provided.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult CreateConnection([FromBody] SavedProfile profile)
    {
        logger.LogInformation("Creating connection.");
        return Ok(connectionManager.CreateConnection(profile));
    }

    /// <summary>
    /// Closes and removes a connection instance by its ID.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the connection instance.</param>
    /// <response code="200">Connection successfully deleted.</response>
    /// <response code="404">Connection instance not found.</response>
    [HttpDelete("{connectionId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteConnection([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Deleting connection.");
        connectionManager.DeleteConnection(connectionId);
        return Ok();
    }

    /// <summary>
    /// Establishes a remote connection for the specified connection instance.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the connection to connect.</param>
    /// <param name="ct">Cancellation token for cancelling the operation.</param>
    /// <response code="200">Successfully connected to the remote host.</response>
    /// <response code="400">Connection attempt failed due to network or authentication issues.</response>
    /// <response code="404">Connection instance not found.</response>
    [HttpPost("{connectionId:guid}/connect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConnectConnection([FromRoute] Guid connectionId, CancellationToken ct)
    {
        logger.LogInformation("Connecting connection.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.ConnectAsync(ct);
        return Ok();
    }

    /// <summary>
    /// Disconnects from the remote host for the specified connection instance.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the connection to disconnect.</param>
    /// <param name="ct">Cancellation token for cancelling the operation.</param>
    /// <response code="200">Successfully disconnected from the remote host.</response>
    /// <response code="404">Connection instance not found.</response>
    [HttpPost("{connectionId:guid}/disconnect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisconnectConnection([FromRoute] Guid connectionId, CancellationToken ct)
    {
        logger.LogInformation("Disconnecting connection.");
        var connection = connectionManager.GetConnection(connectionId);
        await connection.DisconnectAsync(ct);
        return Ok();
    }

    /// <summary>
    /// Checks the current connection status of the specified instance.
    /// </summary>
    /// <param name="connectionId">The unique identifier of the connection instance.</param>
    /// <returns><c>true</c> if connected; otherwise, <c>false</c>.</returns>
    /// <response code="200">Returns current connection state.</response>
    /// <response code="404">Connection instance not found.</response>
    [HttpGet("{connectionId:guid}/state")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetConnectionState([FromRoute] Guid connectionId)
    {
        logger.LogInformation("Getting connection state.");
        var connection = connectionManager.GetConnection(connectionId);
        return Ok(connection.IsConnected);
    }
}
