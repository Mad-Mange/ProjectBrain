// Synthetic fixture. Scanner must not compile this project.
public interface IRepository { string Read(string id); }
public sealed class Repository : IRepository { public string Read(string id) => id; }

