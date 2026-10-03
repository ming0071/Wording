# Render the repo-owned vector mark into PNG-backed, multi-resolution Windows icons.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$taskAssets = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/WordTrail.Desktop/Assets'
[IO.Directory]::CreateDirectory($taskAssets) | Out-Null
$taskImages = @()
foreach ($taskSize in @(16,24,32,48,64,128,256)) {
    $taskBitmap = [Drawing.Bitmap]::new($taskSize, $taskSize)
    $taskGraphics = [Drawing.Graphics]::FromImage($taskBitmap)
    $taskGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $taskGraphics.ScaleTransform($taskSize / 256.0, $taskSize / 256.0)
    $taskShape = [Drawing.Drawing2D.GraphicsPath]::new()
    $taskShape.AddArc(8,8,108,108,180,90)
    $taskShape.AddArc(140,8,108,108,270,90)
    $taskShape.AddArc(140,140,108,108,0,90)
    $taskShape.AddArc(8,140,108,108,90,90)
    $taskShape.CloseFigure()
    $taskPurple = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#4F46E5'))
    $taskGraphics.FillPath($taskPurple, $taskShape)
    $taskPen = [Drawing.Pen]::new([Drawing.Color]::White, 21)
    $taskPen.StartCap = $taskPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $taskPen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $taskPoints = [Drawing.PointF[]]@([Drawing.PointF]::new(57,77),[Drawing.PointF]::new(83,179),[Drawing.PointF]::new(128,119),[Drawing.PointF]::new(173,179),[Drawing.PointF]::new(199,77))
    $taskGraphics.DrawLines($taskPen, $taskPoints)
    $taskMint = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#67E8C5'))
    $taskGraphics.FillEllipse($taskMint, 186,64,26,26)
    $taskStream = [IO.MemoryStream]::new()
    $taskBitmap.Save($taskStream, [Drawing.Imaging.ImageFormat]::Png)
    $taskImages += @{ Size = $taskSize; Bytes = $taskStream.ToArray() }
    if ($taskSize -eq 256) { [IO.File]::WriteAllBytes((Join-Path $taskAssets 'WordTrail.png'), $taskStream.ToArray()) }
    $taskStream.Dispose()
    $taskMint.Dispose()
    $taskPen.Dispose()
    $taskPurple.Dispose()
    $taskShape.Dispose()
    $taskGraphics.Dispose()
    $taskBitmap.Dispose()
}
$taskIcon = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $taskAssets 'WordTrail.ico')))
try {
    $taskIcon.Write([uint16]0)
    $taskIcon.Write([uint16]1)
    $taskIcon.Write([uint16]$taskImages.Count)
    $taskOffset = 6 + 16 * $taskImages.Count
    foreach ($taskImage in $taskImages) {
        $taskDimension = if ($taskImage.Size -eq 256) { 0 } else { $taskImage.Size }
        $taskIcon.Write([byte]$taskDimension)
        $taskIcon.Write([byte]$taskDimension)
        $taskIcon.Write([byte]0)
        $taskIcon.Write([byte]0)
        $taskIcon.Write([uint16]1)
        $taskIcon.Write([uint16]32)
        $taskIcon.Write([uint32]$taskImage.Bytes.Length)
        $taskIcon.Write([uint32]$taskOffset)
        $taskOffset += $taskImage.Bytes.Length
    }
    foreach ($taskImage in $taskImages) { $taskIcon.Write([byte[]]$taskImage.Bytes) }
} finally { $taskIcon.Dispose() }
