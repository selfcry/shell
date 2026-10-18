using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>Команды сервера для окна блокировки: ServerLinkWorker публикует, ControlPipeServer передаёт подключённому окну.</summary>
public sealed class CommandBus
{
    public event Action<PipeMessage>? Published;
    public void Publish(PipeMessage message) => Published?.Invoke(message);
}
