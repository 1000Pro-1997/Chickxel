// UI는 Avalonia(크로스플랫폼: Windows·macOS·Linux)로 만든다.
// 픽셀 좌표(Point/Size/Rectangle)는 Core와 같은 System.Drawing 정수 형식을 쓰고,
// 화면 좌표가 필요하면 Avalonia.Point / Avalonia.Size / Rect를 명시한다.
global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Controls.Primitives;
global using Avalonia.Input;
global using Avalonia.Interactivity;
global using Avalonia.Layout;
global using Avalonia.Media;
global using Avalonia.Media.Imaging;
global using Avalonia.Threading;
global using Point = System.Drawing.Point;
global using Size = System.Drawing.Size;
global using Rectangle = System.Drawing.Rectangle;
global using Color = Avalonia.Media.Color;
// 그라데이션 색 정지점은 Core 형식(Avalonia.Media.GradientStop과 이름이 같다).
global using GradientStop = Chickxel.Core.Edit.GradientStop;
