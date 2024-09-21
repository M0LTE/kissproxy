namespace kissproxy;

internal record Config
{
    public required string Id { get; set; }
    public required string ComPort { get; set; }
    public int Baud { get; set; } = 57600;
    public int TcpPort { get; set; } = 8910;
    public bool AnyHost { get; set; } = false;
    public string? MqttServer { get; set; }
    public string? MqttUsername { get; set; }
    public string? MqttPassword { get; set; }
    public bool Base64 { get; set; } = false;
}