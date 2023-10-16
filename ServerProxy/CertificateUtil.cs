using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ServerProxy
{
    internal class CertificateUtil
    {
        // Installs the root certificate in the local machine's root certificate store.
        static public void Install()
        {

            X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);
            const string Thumbprint = "512cb359732dd45aa6991a795d58d4b3167441d7";
            byte[] RootCA = Encoding.UTF8.GetBytes("MIIECTCCAvECFBV6m5DhYLYm7rH8aSjwgn5zP58HMA0GCSqGSIb3DQEBCwUAMIHAMQswCQYDVQQGEwJDTjESMBAGA1UECAwJR3Vhbmdkb25nMRIwEAYDVQQHDAlHdWFuZ3pob3UxDTALBgNVBAoMBEdaSFUxMzAxBgNVBAsMKkVsZWN0cm9uaWMgSW5mb3JtYXRpb24gTGFib3JhdG9yeSBCdWlsZGluZzETMBEGA1UEAwwKMzEwIFNlcnZlcjEwMC4GCSqGSIb3DQEJARYhbGFiaW5mcmFzdHJ1Y3R1cmUuYm90QG91dGxvb2suY29tMB4XDTIzMTAwNjE2MDIyN1oXDTMzMTAwMzE2MDIyN1owgcAxCzAJBgNVBAYTAkNOMRIwEAYDVQQIDAlHdWFuZ2RvbmcxEjAQBgNVBAcMCUd1YW5nemhvdTENMAsGA1UECgwER1pIVTEzMDEGA1UECwwqRWxlY3Ryb25pYyBJbmZvcm1hdGlvbiBMYWJvcmF0b3J5IEJ1aWxkaW5nMRMwEQYDVQQDDAozMTAgU2VydmVyMTAwLgYJKoZIhvcNAQkBFiFsYWJpbmZyYXN0cnVjdHVyZS5ib3RAb3V0bG9vay5jb20wggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQC23jUMWfPoa/9/GEQBumGNlCSe8gPdq1RHbHLfWM7OGnSFNlePkcZOOk/TlXsKLDZrL+jtfQHN9tFS9ViRjOQu9qoCqjEkqG3sFOMBujz8G2h02axlIaGgObGFzfl7dMABTgHryxJyyEdV72tjaOqaBRR7fb6amrJc5QS8T7JCmV55lSYp42XJaSwHLXx2kLPfKcMf3Gf84n9QBSMg7UuQmtDlbfJbaXLgN4FCVtrJWpPcgJEqL6UVl/3wpBjI8L7PbCAc7vQXf8QTZGgFnZVauDNbR9GkddkaZnhUvWYKRj9f8//bZ5VCnw4nLrgXNFtzjNwPRz2yWsr5G+9+Mp03AgMBAAEwDQYJKoZIhvcNAQELBQADggEBAClo1taS4cXk/ELNfukMHq3OqNrd3Jt2ThLS35NdS596+4C6WyRv8+LrLhZsKjE8/4JdzCsyCf6VHnB48V3ZGMtgux3NBx5hVsZtqy+QML0PriwM8mP4jKLf7LMK9fzMiPQ6YkQNcmgQ7MxQ0zjdiw0UeMvWUMV2WLuiS79e/0Odl854QrnyfFUOrAnbwiId0x4mRmpNPNcg2xZN9qrWVzn6YoQppXzinAzuO3rTj+bDseBLGu/Muo9sK4+JZ59ek40LVjXLxWgRZ4XXzK7tiFWsDxTlhkSFXXPCl2MVtDzoOmTCCTHYERnG2UPM2iiboLh0CUkBhvrz/Y0PMDukYCo=");

            try
            {
                store.Open(OpenFlags.ReadOnly);
                var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint, false);
                if (certificates.Count == 0)
                {
                    MessageBox.Show("没有检测到根证书，程序将自动安装证书", "注意", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    X509Certificate2 certificate = new(RootCA);
                    store.Open(OpenFlags.ReadWrite);
                    store.Add(certificate);
                    MessageBox.Show("证书已被成功安装", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                ExceptionHandler.Handle(ex);
                Environment.Exit(-1);
            }
            finally
            {
                store.Close();
            }
        }
    }

}
