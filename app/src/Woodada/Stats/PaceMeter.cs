namespace Woodada.Stats;

/// <summary>
/// 최근 30분 동안 생선으로 친 몸 길이와 앱이 돌아간 시간. 생선 모으는 속도를 보여 주는 데 쓴다.
/// 파일에 남기지 않으니 켤 때마다 새로 잰다. 시간은 프레임 간격을 더해서 세므로
/// 절전으로 멈춰 있던 시간은 들어가지 않는다.
/// </summary>
public sealed class PaceMeter
{
    private const double BucketSeconds = 60;
    private const int BucketCount = 30;

    // 1분짜리 칸을 돌려 쓴다. 칸이 차면 가장 오래된 칸을 비우고 이어서 쌓는다.
    private readonly double[] _seconds = new double[BucketCount];
    private readonly double[] _bodyLengths = new double[BucketCount];
    private int _head;

    public void Tick(double dt)
    {
        if (_seconds[_head] >= BucketSeconds)
        {
            _head = (_head + 1) % BucketCount;
            _seconds[_head] = 0;
            _bodyLengths[_head] = 0;
        }
        _seconds[_head] += dt;
    }

    public void Add(double bodyLengths) => _bodyLengths[_head] += bodyLengths;

    /// <summary>잰 시간(초). 켠 지 30분이 안 됐으면 켠 뒤로 지난 시간이다.</summary>
    public double Seconds => _seconds.Sum();

    public double BodyLengths => _bodyLengths.Sum();
}
