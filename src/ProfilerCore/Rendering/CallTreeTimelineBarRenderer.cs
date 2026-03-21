using System;
using Spectre.Console;

namespace Asynkron.Profiler;

internal static class CallTreeTimelineBarRenderer
{
    public static string Render(CallTreeNode node, TimelineContext context)
    {
        if (!node.HasTiming || context.RootDuration <= 0)
        {
            return new string(' ', context.BarWidth);
        }

        var buffer = new char[context.BarWidth];
        Array.Fill(buffer, ' ');

        var startOffset = node.MinStart - context.RootStart;
        var startRatio = Math.Clamp(startOffset / context.RootDuration, 0, 1);
        var durationRatio = Math.Clamp((node.MaxEnd - node.MinStart) / context.RootDuration, 0, 1);

        var startPosition = startRatio * context.BarWidth;
        var endPosition = (startRatio + durationRatio) * context.BarWidth;

        var leftIndex = (int)Math.Floor(startPosition);
        var rightIndex = (int)Math.Ceiling(endPosition) - 1;
        leftIndex = Math.Clamp(leftIndex, 0, context.BarWidth - 1);
        rightIndex = Math.Clamp(rightIndex, 0, context.BarWidth - 1);

        if (rightIndex < leftIndex)
        {
            rightIndex = leftIndex;
        }

        if (leftIndex == rightIndex)
        {
            var singleFraction = Math.Clamp(endPosition - startPosition, 0, 1);
            buffer[leftIndex] = singleFraction switch
            {
                >= 0.875 => '█',
                >= 0.625 => '▊',
                >= 0.375 => '▌',
                >= 0.125 => '▎',
                _ => ' '
            };
        }
        else
        {
            var leftFraction = 1 - (startPosition - leftIndex);
            buffer[leftIndex] = SelectLeftBlock(leftFraction);

            for (var index = leftIndex + 1; index < rightIndex; index++)
            {
                buffer[index] = '█';
            }

            var rightFraction = endPosition - Math.Floor(endPosition);
            buffer[rightIndex] = rightFraction <= 0 ? '█' : SelectRightBlock(rightFraction);
        }

        var heat = Math.Clamp((node.MaxEnd - node.MinStart) / context.RootDuration, 0, 1);
        var color = GetHeatColor(heat);
        return $"[{color}]{Markup.Escape(new string(buffer))}[/]";
    }

    private static string GetHeatColor(double percentage)
    {
        return percentage switch
        {
            >= 0.75 => "red",
            >= 0.50 => "orange1",
            >= 0.25 => "yellow1",
            _ => "grey"
        };
    }

    private static char SelectLeftBlock(double fraction)
    {
        return fraction switch
        {
            >= 1.0 => '█',
            >= 0.875 => '▉',
            >= 0.75 => '▊',
            >= 0.625 => '▋',
            >= 0.5 => '▌',
            >= 0.375 => '▍',
            >= 0.25 => '▎',
            >= 0.125 => '▏',
            _ => ' '
        };
    }

    private static char SelectRightBlock(double fraction)
    {
        return fraction switch
        {
            >= 1.0 => '█',
            >= 0.5 => '▐',
            >= 0.125 => '▕',
            _ => ' '
        };
    }
}
