{{/* The release name IS the service name: "order", "inventory", ...
     This matters: it becomes the k8s Service DNS name (http://inventory:8081) and the
     ServiceAccount name that the Azure federated credential trusts on Day 4. */}}
{{- define "svc.name" -}}
{{- .Release.Name -}}
{{- end -}}

{{/* Selector labels must NEVER change after the first install (Deployment selectors are immutable). */}}
{{- define "svc.selectorLabels" -}}
app.kubernetes.io/name: {{ include "svc.name" . }}
app.kubernetes.io/part-of: orderflow
{{- end -}}

{{- define "svc.labels" -}}
{{ include "svc.selectorLabels" . }}
app.kubernetes.io/version: {{ .Values.image.tag | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{- define "svc.image" -}}
{{- if .Values.image.registry -}}
{{ .Values.image.registry }}/{{ .Values.image.repository }}:{{ .Values.image.tag }}
{{- else -}}
{{ .Values.image.repository }}:{{ .Values.image.tag }}
{{- end -}}
{{- end -}}
