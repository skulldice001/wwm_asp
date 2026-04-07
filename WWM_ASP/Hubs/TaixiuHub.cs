using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WWM_ASP.Hubs;

[Authorize(AuthenticationSchemes = "user")]
public class TaixiuHub : Hub
{
    public Task JoinLobby()
        => Groups.AddToGroupAsync(Context.ConnectionId, "taixiu-lobby");

    public Task LeaveLobby()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, "taixiu-lobby");

    public Task JoinRoom(long tableId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"taixiu-room-{tableId}");

    public Task LeaveRoom(long tableId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"taixiu-room-{tableId}");
}
