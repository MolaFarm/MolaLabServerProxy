using System;

namespace Ae.Dns.Client.Exceptions
{
    /// <summary>
    /// An exception thrown when a DNS request times out.
    /// </summary>
    public class DnsClientException : Exception
    {
        /// <summary>
        /// Construct a new <see cref="DnsClientTimeoutException"/> using the specified timeout and domain name.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="domain">The related DNS query.</param>
        public DnsClientException(string message, string domain)
            : base($"{message} for {domain}")
        {
            Domain = domain;
        }

        /// <summary>
        /// The domain which timed out.
        /// </summary>
        public string Domain { get; }
    }
}
