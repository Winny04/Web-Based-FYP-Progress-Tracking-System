using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FYPTracker.Hubs
{
    public class NotificationHub : Hub
    {
        // Tracks UserId -> HashSet of ConnectionIds
        private static readonly ConcurrentDictionary<int, HashSet<string>> _onlineUsers = new();
        // Tracks ConnectionId -> UserId for easy cleanup on disconnect
        private static readonly ConcurrentDictionary<string, int> _connectionUserMap = new();

        public async Task JoinUserGroup(int userId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");
            
            _connectionUserMap[Context.ConnectionId] = userId;
            
            bool isNewlyOnline = false;
            _onlineUsers.AddOrUpdate(userId,
                id => {
                    isNewlyOnline = true;
                    return new HashSet<string> { Context.ConnectionId };
                },
                (id, connections) => {
                    lock (connections)
                    {
                        if (connections.Count == 0) isNewlyOnline = true;
                        connections.Add(Context.ConnectionId);
                    }
                    return connections;
                });

            if (isNewlyOnline)
            {
                // Broadcast to everyone that this user is now online
                await Clients.All.SendAsync("UserOnline", userId);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_connectionUserMap.TryRemove(Context.ConnectionId, out int userId))
            {
                if (_onlineUsers.TryGetValue(userId, out var connections))
                {
                    bool isNowOffline = false;
                    lock (connections)
                    {
                        connections.Remove(Context.ConnectionId);
                        if (connections.Count == 0)
                        {
                            isNowOffline = true;
                        }
                    }

                    if (isNowOffline)
                    {
                        // Clean up empty hashset
                        _onlineUsers.TryRemove(userId, out _);
                        // Broadcast to everyone that this user is now offline
                        await Clients.All.SendAsync("UserOffline", userId);
                    }
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        public List<int> GetOnlineUsers()
        {
            // Returns a list of all currently online user IDs
            return _onlineUsers.Keys.ToList();
        }

        public async Task SendTypingIndicator(int receiverId, string senderName)
        {
            await Clients.Group($"User_{receiverId}").SendAsync("ReceiveTypingIndicator", senderName);
        }
    }
}
