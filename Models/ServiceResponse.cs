using System.Collections.Generic;

public class ServiceResponse<T>
{
    public T Data { get; set; }
    public bool Success { get; set; } = true;
    public string Message { get; set; } = null;
    public List<string> Errors { get; set; } = new List<string>();
}