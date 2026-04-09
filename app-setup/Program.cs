using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Pulumi;
using Pulumi.Kubernetes;
using Pulumi.Kubernetes.Core.V1;
using Pulumi.Kubernetes.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Meta.V1;


return await Deployment.RunAsync(async () =>
{
    var config = new Pulumi.Config();
    var kubeconfig = config.RequireSecret("kubeconfig");

    var provider = new Provider("k8sProvider", new ProviderArgs
    {
        KubeConfig = kubeconfig
    });

    var stackref = new StackReference("pierskarsenbarg/eso-demo-k8s-setup/dev");

    var escSecretStore = stackref.GetOutput("EscSecretName");
    var awsSecretStore = stackref.GetOutput("AwsStoreName");
    var azureSecretStore = stackref.GetOutput("AzureStoreName");

    var clusterSecretStoreKind = "ClusterSecretStore";

    var appNamespace = new Namespace("app-namespace", null, new() { Provider = provider });

    var escSecret = new Pulumi.Kubernetes.ApiExtensions.CustomResource("esc-external-secret", new ExternalSecretArgs(apiVersion: "external-secrets.io/v1", kind: "ExternalSecret")
    {
        Metadata = new ObjectMetaArgs
        {
            Namespace = appNamespace.Metadata.Apply(m => m.Name)
        },
        Spec = new InputMap<object>
        {
            ["refreshInterval"] = "15s",
            ["secretStoreRef"] = new Dictionary<string, object>
            {
                ["kind"] = clusterSecretStoreKind,
                ["name"] = escSecretStore
            },
            ["dataFrom"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["extract"] = new Dictionary<string, object>
                    {
                        ["conversionStrategy"] = "Default",
                        ["key"] = "esc-app"
                    }
                }
            }
        }
    }, new() { Provider = provider });

    var azureSecret = new Pulumi.Kubernetes.ApiExtensions.CustomResource("azure-external-secret", new ExternalSecretArgs(apiVersion: "external-secrets.io/v1", kind: "ExternalSecret")
    {
        Metadata = new ObjectMetaArgs
        {
            Namespace = appNamespace.Metadata.Apply(m => m.Name)
        },
        Spec = new InputMap<object>
        {
            ["refreshInterval"] = "15s",
            ["secretStoreRef"] = new Dictionary<string, object>
            {
                ["kind"] = clusterSecretStoreKind,
                ["name"] = azureSecretStore
            },
            ["dataFrom"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["extract"] = new Dictionary<string, object>
                    {
                        ["conversionStrategy"] = "Default",
                        ["key"] = "azure-app"
                    }
                }
            }
        }
    }, new() { Provider = provider });

    var podInfo = new Release("podinfo", new ReleaseArgs()
    {
        Namespace = appNamespace.Metadata.Apply(n => n.Name),
        Chart = "podinfo",
        RepositoryOpts = new RepositoryOptsArgs()
        {
            Repo = "https://stefanprodan.github.io/podinfo"
        },
        Values = new InputMap<object>
        {
            ["replicaCount"] = 2,
            ["extraEnvs"] = new[]
            {
                new InputMap<object>
                {
                    ["name"] = "PULUMI_ACCESS_TOKEN",
                    ["valueFrom"] = new InputMap<object>
                    {
                        ["secretKeyRef"] = new InputMap<object>
                        {
                            ["name"] = escSecret.Metadata.Apply(n => n.Name),
                            ["key"] = "pulumiAccessToken"
                        }
                    }
                }
            }
        }
    }, new CustomResourceOptions()
    {
        Provider = provider,
    });

    var port = Service.Get("podInfoService", Output.Format($"{appNamespace.Metadata.Apply(x => x.Name)}/{podInfo.Name}"))
                            .Spec.Apply(x => x.Ports.Where(x => x?.TargetPort == "http").Select(x => x.Port).FirstOrDefault());

    return new Dictionary<string, object?>
    {
        ["serviceName"] = podInfo.Name,
        ["port"] = port,
        ["namespace"] = appNamespace.Metadata.Apply(x => x.Name)
    };
});

class ExternalSecretArgs : Pulumi.Kubernetes.ApiExtensions.CustomResourceArgs
{
    public ExternalSecretArgs(string apiVersion, string kind) : base(apiVersion, kind)
    {
    }

    [Input("spec")]
    public Input<object>? Spec { get; set; }
}