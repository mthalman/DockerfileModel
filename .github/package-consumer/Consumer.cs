using System;
using System.Linq;
using Valleysoft.DockerfileModel;

public static class Consumer
{
    public static string ParseAndModify(string text)
    {
        Dockerfile dockerfile = Dockerfile.Parse(text);
        if (dockerfile.ToString() != text)
        {
            throw new InvalidOperationException("The package did not preserve the Dockerfile text.");
        }
        FromInstruction from = dockerfile.Items.OfType<FromInstruction>().First();
        ImageName image = ImageName.Parse(from.ImageName);
        image.Tag = "latest";
        from.ImageName = image.ToString();
        return dockerfile.ToString();
    }
}
