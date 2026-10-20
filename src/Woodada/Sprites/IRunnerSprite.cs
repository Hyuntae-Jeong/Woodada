namespace Woodada.Sprites;

/// <summary>한 프레임을 그리는 데 필요한 러너 자세.</summary>
/// <param name="Facing">+1 오른쪽, -1 왼쪽.</param>
/// <param name="GaitPhase">0~1 보행 위상.</param>
/// <param name="Intensity">0~1 사용률. 보폭·몸 기울기에 쓴다.</param>
public readonly record struct RunnerPose(int Facing, double GaitPhase, double Intensity);

/// <summary>
/// 러너 종류 하나(고양이, 강아지 …). 새 러너는 이 인터페이스를 구현해서 SpriteRegistry에 등록하면
/// 설정 화면과 오버레이에 자동으로 붙는다.
/// </summary>
public interface IRunnerSprite
{
    string Id { get; }
    string DisplayName { get; }

    /// <summary>높이가 heightPx일 때 차지하는 박스 크기.</summary>
    SizeF Measure(int heightPx);

    /// <summary>box 안에 그린다. 발은 box 아래쪽 선에 닿는다.</summary>
    void Draw(Graphics g, RectangleF box, RunnerPose pose, Color color);
}
