using Android.Content;
using Android.Views;

namespace GameStream.Client.Ui;

/// <summary>SurfaceView que mantém a proporção do vídeo (letterbox centralizado).</summary>
public class AspectSurfaceView : SurfaceView
{
    private float _aspect;

    public AspectSurfaceView(Context context) : base(context)
    {
    }

    public void SetAspect(int width, int height)
    {
        _aspect = height > 0 ? (float)width / height : 0f;
        RequestLayout();
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        int w = MeasureSpec.GetSize(widthMeasureSpec);
        int h = MeasureSpec.GetSize(heightMeasureSpec);

        if (_aspect > 0f && w > 0 && h > 0)
        {
            if (w / (float)h > _aspect) w = (int)(h * _aspect);
            else h = (int)(w / _aspect);
        }
        SetMeasuredDimension(w, h);
    }
}
