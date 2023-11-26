using System.Text;

namespace Protocal.Request.Http;

public class HttpRequest
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string RequestType { get; set; }
    public string RequestData { get; set; }

    public static HttpRequest? FromBytes(byte[] buffer, int length)
    {
        var httpRequest = new HttpRequest();

        // Read the request line
        try
        {
            var requstData = Encoding.UTF8.GetString(buffer, 0, length);
            if (string.IsNullOrEmpty(requstData.Trim())) return null;
            var lines = requstData.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("Proxy-Connection")) continue;
                httpRequest.RequestData += line;
                if (lines.Last() != line) httpRequest.RequestData += "\n";
            }

            var requestParts = lines[0].Trim().Split(' ');
            httpRequest.RequestType = requestParts[0];
            if (httpRequest.RequestType.Equals("CONNECT"))
            {
                var urlParts = requestParts[1].Trim().Split(":");
                httpRequest.Host = urlParts[0];
                httpRequest.Port = Convert.ToInt32(urlParts[1]);
            }
            else
            {
                var url = new Uri(requestParts[1]);
                httpRequest.Host = url.Host;
                httpRequest.Port = url.Port;
            }

            return httpRequest;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Request is invalid", ex);
        }
    }
}