# cert-manager ClusterIssuers

Two ACME (Let's Encrypt) `ClusterIssuer`s for automated TLS on the gateway ingress:

| File | Issuer | Use |
|------|--------|-----|
| `cluster-issuer-staging.yaml` | `letsencrypt-staging` | **Always first.** Generous rate limits; untrusted root (browsers warn, use `curl -k`). Validate the whole flow here. |
| `cluster-issuer-prod.yaml` | `letsencrypt-prod` | Browser-trusted, but **strict weekly rate limits**. Only after staging issues successfully. |

Both use the **HTTP-01** solver against the `nginx` ingress class, which works with the
public hostname pointed at the ingress controller's IP — the custom domain
`api.antkart.in` (or a `<public-ip>.nip.io` fallback where no domain exists).

**Before applying:** replace the placeholder `email:` in each file with a monitored
address (Let's Encrypt sends expiry notices there). It is a placeholder on purpose —
do not commit a personal address to a public repo.

**To switch an ingress from staging to production**, change the gateway's
`ingress.clusterIssuer` from `letsencrypt-staging` to `letsencrypt-prod`, delete the
old TLS secret so a fresh certificate is requested, and `helm upgrade`. See the full
runbook in [docs/guides/aks-guide.md](../../docs/guides/aks-guide.md#ingress-and-tls).
