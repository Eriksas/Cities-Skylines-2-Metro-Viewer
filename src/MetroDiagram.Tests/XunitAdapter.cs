using Xunit;

// Exposes the TestSuite catalog to `dotnet test` / IDE test runners: every
// entry becomes one theory case named after its catalog entry. The classic
// `dotnet run` runner (TestSuite.RunAll) stays the source of truth; this
// adapter only changes how the same delegates are invoked and reported.
public class XunitAdapter
{
    public static TheoryData<string> TestNames()
    {
        TheoryData<string> data = [];
        foreach ((string name, _) in TestSuite.tests)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TestNames))]
    public void Run(string name)
    {
        Action test = TestSuite.tests.Single(entry => entry.Name == name).Test;
        test();
    }
}
