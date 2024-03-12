namespace Protocol.Tools;

public class RTO
{
    private readonly double Alpha = 0.125;
    private readonly double Beta = 0.25;
    private readonly double ClockGranularity;
    private readonly double MinimumRTO;
    private double BackoffFactor = 1;
    private double CurrentRTO;
    private double RTTVAR;
    private double SRTT;

    public RTO(double minRTO, double clockGranularity)
    {
        MinimumRTO = minRTO;
        ClockGranularity = clockGranularity;
    }

    public double GetRTO(double RTT, bool retransmitted)
    {
        if (retransmitted)
        {
            BackoffFactor *= 2;
            CurrentRTO *= BackoffFactor;
        }
        else
        {
            // Update SRTT and RTTVAR 
            if (SRTT == 0)
            {
                SRTT = RTT;
                RTTVAR = RTT / 2;
            }
            else
            {
                RTTVAR = (1 - Beta) * RTTVAR + Beta * Math.Abs(SRTT - RTT);
                SRTT = (1 - Alpha) * SRTT + Alpha * RTT;
            }

            BackoffFactor = 1;
            CurrentRTO = Math.Max(MinimumRTO, SRTT + Math.Max(ClockGranularity, 4 * RTTVAR));
        }

        return CurrentRTO;
    }
}