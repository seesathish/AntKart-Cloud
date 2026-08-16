{{/*
Resource name — the ak-<service> identity. Required; fail fast if unset so a
misconfigured values file cannot render nameless objects.
*/}}
{{- define "antkart-service.name" -}}
{{- required "values.name (e.g. ak-products) is required" .Values.name -}}
{{- end -}}

{{/*
Fully-qualified container image. The image PATH segment is image.name when set,
otherwise it defaults to serviceName. These are deliberately decoupled: serviceName
drives the in-cluster identity (ServiceAccount ak-<serviceName>) which is bound to
the federated identity credential subject, while the image path must match whatever
repository actually exists in ACR — the two are not always the same (e.g. cart).
*/}}
{{- define "antkart-service.image" -}}
{{- $svc := required "values.serviceName (e.g. products) is required" .Values.serviceName -}}
{{- $name := default $svc .Values.image.name -}}
{{- printf "%s/antkart/%s:%s" .Values.image.registry $name .Values.image.tag -}}
{{- end -}}

{{/*
Common labels.
*/}}
{{- define "antkart-service.labels" -}}
app.kubernetes.io/name: {{ include "antkart-service.name" . }}
app.kubernetes.io/part-of: antkart
app.kubernetes.io/component: {{ .Values.serviceName | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{/*
Selector labels (stable — must not include version/tag).
*/}}
{{- define "antkart-service.selectorLabels" -}}
app.kubernetes.io/name: {{ include "antkart-service.name" . }}
{{- end -}}
