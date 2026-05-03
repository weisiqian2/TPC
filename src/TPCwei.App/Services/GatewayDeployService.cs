using System.Text.Json;
using TPC.App.Models;

namespace TPC.App.Services;

public sealed class GatewayDeployPlan
{
    public string WindowsCommand { get; init; } = "";
    public string LinuxSystemdCommand { get; init; } = "";
    public string DockerCommand { get; init; } = "";
    public string GatewayJson { get; init; } = "";
}

public sealed class GatewayDeployService
{
    public GatewayDeployPlan CreatePlan(GatewayDeployOptions options)
    {
        var token = string.IsNullOrWhiteSpace(options.Token) ? "<请改成强令牌>" : options.Token;
        var gatewayJson = JsonSerializer.Serialize(new
        {
            bind = "0.0.0.0",
            controlPort = options.ControlPort,
            adminBind = "127.0.0.1",
            adminPort = options.AdminPort,
            token,
            logPath = "tpcwei_gateway.log"
        }, new JsonSerializerOptions { WriteIndented = true });

        return new GatewayDeployPlan
        {
            WindowsCommand = $"tpcwei_gateway.exe --bind 0.0.0.0 --control-port {options.ControlPort} --admin-port {options.AdminPort} --token {token}",
            LinuxSystemdCommand =
                "sudo tee /etc/systemd/system/tpcwei-gateway.service >/dev/null <<'EOF'\n" +
                "[Unit]\nDescription=TPCwei Gateway\nAfter=network-online.target\n\n" +
                "[Service]\nExecStart=/usr/local/bin/tpcwei_gateway --bind 0.0.0.0 --control-port " + options.ControlPort + " --admin-port " + options.AdminPort + " --token " + token + "\n" +
                "Restart=always\nRestartSec=3\n\n" +
                "[Install]\nWantedBy=multi-user.target\nEOF\n" +
                "sudo systemctl daemon-reload && sudo systemctl enable --now tpcwei-gateway",
            DockerCommand = $"docker run -d --name tpcwei-gateway --restart unless-stopped -p {options.ControlPort}:{options.ControlPort}/tcp -p {options.AdminPort}:{options.AdminPort}/tcp tpcwei/gateway:latest --bind 0.0.0.0 --control-port {options.ControlPort} --admin-port {options.AdminPort} --token {token}",
            GatewayJson = gatewayJson
        };
    }
}
