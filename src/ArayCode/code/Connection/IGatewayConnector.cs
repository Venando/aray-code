public interface IGatewayConnector
{
    public Task ConnectAsync(CancellationToken ct);
}