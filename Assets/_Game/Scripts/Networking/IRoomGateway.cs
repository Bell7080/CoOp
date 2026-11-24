using System.Threading.Tasks;

namespace CoOpHorror.Networking
{
    // Room discovery/connectivity lives here; gameplay remains NGO NetworkBehaviours.
    // A future Steam implementation must configure a compatible NGO transport.
    public interface IRoomGateway
    {
        string JoinCode { get; }
        bool HasRoom { get; }
        Task HostAsync();
        Task JoinAsync(string code);
        Task LeaveAsync();
    }
}
