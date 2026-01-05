using Xunit;

namespace Asynkron.Profiler.Tests;

public sealed class NameFormatterTests
{
    [Theory]
    [InlineData("Program+<<<Main>$>g__EvaluateAsync|0_2>d.MoveNext", "StateMachine.EvaluateAsync.MoveNext")]
    [InlineData("Program+<<Main>$>d__0.MoveNext", "StateMachine.Main.MoveNext")]
    [InlineData("PromiseConstructor.<AttachStatics>b__5_0", "PromiseConstructor.AttachStatics lambda")]
    [InlineData("Asynkron.JsEngine.Ast.TypedAstEvaluator+TypedFunction.InvokeWithContext2(System.Int32)",
        "TypedAstEvaluator.TypedFunction.InvokeWithContext2")]
    [InlineData("UNMANAGED_CODE_TIME", "Unmanaged Code")]
    [InlineData("UNMANAGED_CODE_TIME (Native Frames)", "Unmanaged Code")]
    [InlineData("0)", "Unmanaged Code")]
    [InlineData("0[]&,int32)", "Unmanaged Code")]
    [InlineData("0&,unsigned int)", "Unmanaged Code")]
    public void FormatsMethodNames(string raw, string expected)
    {
        var actual = NameFormatter.FormatMethodDisplayName(raw);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("System.Collections.Generic.Dictionary`2[System.String,System.Int32]", "Dictionary<String,Int32>")]
    [InlineData("System.String[]", "String[]")]
    public void FormatsTypeNames(string raw, string expected)
    {
        var actual = NameFormatter.FormatTypeDisplayName(raw);
        Assert.Equal(expected, actual);
    }
}
