namespace GHOST.SqlServer.Tests;

public sealed class ScratchDbGuardTests
{
    [Theory]
    [InlineData("Server=.;Database=GHOST2;Integrated Security=True")]
    [InlineData("Server=.;Database=GHOST;Integrated Security=True")]
    [InlineData("Server=.;Database=GHOST_PROD;Integrated Security=True")]
    [InlineData("Server=.;Database=master;Integrated Security=True")]
    [InlineData("Server=.;Integrated Security=True")]
    [InlineData("Server=.;Database=GHOST_SCRATCH_x;AttachDBFilename=c:\\x.mdf")]
    public void Real_or_unknown_databases_are_refused(string connection)
        => Assert.Throws<InvalidOperationException>(() => ScratchDb.ValidateAndNormalize(connection));

    [Theory]
    [InlineData("Server=.;Database=GHOST_SCRATCH_20261121;Integrated Security=True")]
    [InlineData("Server=.;Database=GHOST_IT_run1;Integrated Security=True")]
    public void Scratch_names_are_accepted(string connection)
        => Assert.NotEmpty(ScratchDb.ValidateAndNormalize(connection));
}
