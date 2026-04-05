namespace CodeDesigner.Ps2.Multiplayer.Models;

/// <summary>A lobby channel that players can join and chat in.</summary>
public class MediusChannel
{
    public int    ChannelId        { get; set; }
    public string ChannelName      { get; set; } = string.Empty;
    public int    MaxPlayers       { get; set; } = 256;
    public int    AppId            { get; set; }
    public string Password         { get; set; } = string.Empty;
    public int    GenericField1    { get; set; }
    public int    GenericField2    { get; set; }
    public int    GenericField3    { get; set; }

    private readonly List<int> _playerIds = new();

    public IReadOnlyList<int> PlayerIds => _playerIds;
    public int PlayerCount => _playerIds.Count;
    public bool IsFull => PlayerCount >= MaxPlayers;

    public void AddPlayer(int accountId)    { if (!_playerIds.Contains(accountId)) _playerIds.Add(accountId); }
    public void RemovePlayer(int accountId) { _playerIds.Remove(accountId); }
}
