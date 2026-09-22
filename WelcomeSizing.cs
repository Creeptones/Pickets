using System;
using System.Windows;

namespace Pickets;

internal static class WelcomeSizing
{
    internal static Size Available(double pixelWidth, double pixelHeight, double scaleX, double scaleY)
        => new(Math.Max(1, pixelWidth / scaleX - 24), Math.Max(1, pixelHeight / scaleY - 24));
}
