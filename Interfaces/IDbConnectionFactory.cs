using System.Data;

namespace EntityBuilder.Interfaces;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}
