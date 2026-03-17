namespace Valleysoft.DockerfileModel.Tests;

public class StagesViewTest
{
    [Fact]
    public void SingleStage()
    {
        List<string> lines = new()
        {
            "FROM image\n",
            "ARG test=a\n",
            "RUN echo 1"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

        StagesView stagesView = new(dockerfile);

        Assert.Empty(stagesView.GlobalArgs);
        Assert.Empty(stagesView.GlobalItems);

        Assert.Single(stagesView.Stages);
        Stage stage = stagesView.Stages.First();
        Assert.Null(stage.Name);
        Assert.Equal(lines[0], stage.FromInstruction.ToString());
        Assert.Equal(2, stage.Items.Count());
        Assert.Equal(lines[1], stage.Items.First().ToString());
        Assert.Equal(lines[2], stage.Items.Last().ToString());
    }

    [Fact]
    public void SingleStageWithGlobalArgs()
    {
        List<string> lines = new()
        {
            "ARG A\n",
            "ARG B\n",
            "#comment\n",
            "\n",
            "FROM image\n",
            "#comment2\n",
            "ARG test=a\n",
            "RUN echo 1"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

        StagesView stagesView = new(dockerfile);

        Assert.Equal(2, stagesView.GlobalArgs.Count());
        Assert.Equal(lines[0], stagesView.GlobalArgs.First().ToString());
        Assert.Equal(lines[1], stagesView.GlobalArgs.Last().ToString());

        Assert.Equal(4, stagesView.GlobalItems.Count());
        Assert.Equal(lines[0], stagesView.GlobalItems.ElementAt(0).ToString());
        Assert.Equal(lines[1], stagesView.GlobalItems.ElementAt(1).ToString());
        Assert.IsType<Comment>(stagesView.GlobalItems.ElementAt(2));
        Assert.Equal(lines[2], stagesView.GlobalItems.ElementAt(2).ToString());
        Assert.IsType<Whitespace>(stagesView.GlobalItems.ElementAt(3));
        Assert.Equal(lines[3], stagesView.GlobalItems.ElementAt(3).ToString());

        Assert.Single(stagesView.Stages);
        Stage stage = stagesView.Stages.First();
        Assert.Null(stage.Name);
        Assert.Equal(lines[4], stage.FromInstruction.ToString());
        Assert.Equal(3, stage.Items.Count());
        Assert.Equal(lines[5], stage.Items.First().ToString());
        Assert.Equal(lines[6], stage.Items.ElementAt(1).ToString());
        Assert.Equal(lines[7], stage.Items.Last().ToString());
    }

    [Fact]
    public void MultiStage()
    {
        List<string> lines = new()
        {
            "FROM image AS stage1\n",
            "RUN echo 1\n",
            "FROM image2 AS stage2\n",
            "RUN echo 2\n",
            "#comment\n",
            "FROM image3 AS stage3\n",
            "RUN echo 3"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

        StagesView stagesView = new(dockerfile);

        Assert.Empty(stagesView.GlobalArgs);
        Assert.Empty(stagesView.GlobalItems);

        Assert.Equal(3, stagesView.Stages.Count());

        Stage stage1 = stagesView.Stages.First();
        Assert.Equal("stage1", stage1.Name);
        Assert.Equal(lines[0], stage1.FromInstruction.ToString());
        Assert.Single(stage1.Items);
        Assert.Equal(lines[1], stage1.Items.First().ToString());

        Stage stage2 = stagesView.Stages.ElementAt(1);
        Assert.Equal("stage2", stage2.Name);
        Assert.Equal(lines[2], stage2.FromInstruction.ToString());
        Assert.Equal(2, stage2.Items.Count());
        Assert.Equal(lines[3], stage2.Items.First().ToString());
        Assert.Equal(lines[4], stage2.Items.Last().ToString());

        Stage stage3 = stagesView.Stages.Last();
        Assert.Equal("stage3", stage3.Name);
        Assert.Equal(lines[5], stage3.FromInstruction.ToString());
        Assert.Single(stage3.Items);
        Assert.Equal(lines[6], stage3.Items.First().ToString());
    }

    [Fact]
    public void MultiStageWithGlobalArgs()
    {
        List<string> lines = new()
        {
            "ARG ARG1\n",
            "FROM image AS stage1\n",
            "RUN echo 1\n",
            "FROM image2 AS stage2\n",
            "RUN echo 2\n",
            "#comment\n",
            "FROM image3 AS stage3"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

        StagesView stagesView = new(dockerfile);

        Assert.Single(stagesView.GlobalArgs);
        Assert.Equal(lines[0], stagesView.GlobalArgs.First().ToString());

        Assert.Equal(3, stagesView.Stages.Count());

        Stage stage1 = stagesView.Stages.First();
        Assert.Equal("stage1", stage1.Name);
        Assert.Equal(lines[1], stage1.FromInstruction.ToString());
        Assert.Single(stage1.Items);
        Assert.Equal(lines[2], stage1.Items.First().ToString());

        Stage stage2 = stagesView.Stages.ElementAt(1);
        Assert.Equal("stage2", stage2.Name);
        Assert.Equal(lines[3], stage2.FromInstruction.ToString());
        Assert.Equal(2, stage2.Items.Count());
        Assert.Equal(lines[4], stage2.Items.First().ToString());
        Assert.Equal(lines[5], stage2.Items.Last().ToString());

        Stage stage3 = stagesView.Stages.Last();
        Assert.Equal("stage3", stage3.Name);
        Assert.Equal(lines[6], stage3.FromInstruction.ToString());
        Assert.Empty(stage3.Items);
    }

    [Fact]
    public void CommentsBeforeFirstFrom()
    {
        List<string> lines = new()
        {
            "# This is a header comment\n",
            "# Another comment\n",
            "FROM image\n",
            "RUN echo 1"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

        StagesView stagesView = new(dockerfile);

        Assert.Empty(stagesView.GlobalArgs);
        Assert.Equal(2, stagesView.GlobalItems.Count());
        Assert.IsType<Comment>(stagesView.GlobalItems.ElementAt(0));
        Assert.Equal(lines[0], stagesView.GlobalItems.ElementAt(0).ToString());
        Assert.IsType<Comment>(stagesView.GlobalItems.ElementAt(1));
        Assert.Equal(lines[1], stagesView.GlobalItems.ElementAt(1).ToString());

        Assert.Single(stagesView.Stages);
        Stage stage = stagesView.Stages.First();
        Assert.Equal(lines[2], stage.FromInstruction.ToString());
        Assert.Single(stage.Items);
        Assert.Equal(lines[3], stage.Items.First().ToString());
    }

    [Fact]
    public void CommentsAndArgsBeforeFirstFrom()
    {
        List<string> lines = new()
        {
            "# Build configuration\n",
            "ARG VERSION=latest\n",
            "# Another comment\n",
            "ARG BASE=alpine\n",
            "FROM ${BASE}:${VERSION}\n",
            "RUN echo 1"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

        StagesView stagesView = new(dockerfile);

        Assert.Equal(2, stagesView.GlobalArgs.Count());
        Assert.Equal(4, stagesView.GlobalItems.Count());
        Assert.IsType<Comment>(stagesView.GlobalItems.ElementAt(0));
        Assert.Equal(lines[0], stagesView.GlobalItems.ElementAt(0).ToString());
        Assert.IsType<ArgInstruction>(stagesView.GlobalItems.ElementAt(1));
        Assert.Equal(lines[1], stagesView.GlobalItems.ElementAt(1).ToString());
        Assert.IsType<Comment>(stagesView.GlobalItems.ElementAt(2));
        Assert.Equal(lines[2], stagesView.GlobalItems.ElementAt(2).ToString());
        Assert.IsType<ArgInstruction>(stagesView.GlobalItems.ElementAt(3));
        Assert.Equal(lines[3], stagesView.GlobalItems.ElementAt(3).ToString());

        Assert.Single(stagesView.Stages);
        Stage stage = stagesView.Stages.First();
        Assert.Equal(lines[4], stage.FromInstruction.ToString());
    }

    [Fact]
    public void OnlyCommentsBeforeFirstFrom()
    {
        List<string> lines = new()
        {
            "# Just a comment\n",
            "FROM image"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

        StagesView stagesView = new(dockerfile);

        Assert.Empty(stagesView.GlobalArgs);
        Assert.Single(stagesView.GlobalItems);
        Assert.IsType<Comment>(stagesView.GlobalItems.First());
        Assert.Equal(lines[0], stagesView.GlobalItems.First().ToString());

        Assert.Single(stagesView.Stages);
    }

    [Fact]
    public void ParserDirectivesBeforeFirstFrom()
    {
        List<string> lines = new()
        {
            "# syntax=docker/dockerfile:1\n",
            "# escape=`\n",
            "ARG VERSION=latest\n",
            "FROM image"
        };

        Dockerfile dockerfile = Dockerfile.Parse(String.Join("", lines.ToArray()));

        StagesView stagesView = new(dockerfile);

        Assert.Single(stagesView.GlobalArgs);
        Assert.Equal(lines[2], stagesView.GlobalArgs.First().ToString());

        Assert.Single(stagesView.GlobalItems);
        Assert.IsType<ArgInstruction>(stagesView.GlobalItems.ElementAt(0));
        Assert.Equal(lines[2], stagesView.GlobalItems.ElementAt(0).ToString());

        Assert.Single(stagesView.Stages);
        Assert.Equal(lines[3], stagesView.Stages.First().FromInstruction.ToString());
    }
}
