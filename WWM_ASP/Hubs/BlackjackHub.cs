using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WWM_ASP.Hubs;

[Authorize(AuthenticationSchemes = "user")]
public class BlackjackHub : Hub
{
    public Task JoinLobby()
        => Groups.AddToGroupAsync(Context.ConnectionId, "blackjack-lobby");

    public Task LeaveLobby()
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, "blackjack-lobby");

    public Task JoinRoom(long tableId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"blackjack-room-{tableId}");

    public Task LeaveRoom(long tableId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"blackjack-room-{tableId}");
}
