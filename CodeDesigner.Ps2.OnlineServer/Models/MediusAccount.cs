namespace CodeDesigner.Ps2.OnlineServer.Models;

/// <summary>An in-memory account record.</summary>
public class MediusAccount
{
    public int    AccountId       { get; set; }
    public string AccountName     { get; set; } = string.Empty;
    public string PasswordHash    { get; set; } = string.Empty;  // SHA-1 hex or plain for dev
    public string Stats           { get; set; } = string.Empty;
    public bool   IsBanned        { get; set; }
    public DateTime CreatedAt     { get; set; } = DateTime.UtcNow;
}
