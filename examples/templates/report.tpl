{# Server Health Report Template #}
{# Demonstrates: variables, filters, conditionals, loops, loop metadata, whitespace control #}

============================================================
  {{ title | upper }}
  Generated: {{ generated }}
============================================================

Summary
-------
  Total Servers: {{ servers | length }}
  Healthy:       {{ healthy_count }}
  Unhealthy:     {{ unhealthy_count }}
  Health Rate:   {{ health_pct }}%

{% if unhealthy_count > 0 -%}
⚠ ATTENTION: {{ unhealthy_count }} server(s) require investigation.
{%- endif %}

Server Details
--------------
{% for srv in servers -%}
  {{ loop.index }}. {{ srv.name | upper }}
     Host:    {{ srv.host }}
     Port:    {{ srv.port }}
     Status:  {{ srv.status }}
     Uptime:  {{ srv.uptime ?? "N/A" }}
     Load:    {{ srv.load ?? "unknown" }}
     {% if srv.status == "down" -%}
     *** THIS SERVER IS DOWN — CHECK IMMEDIATELY ***
     {%- elif srv.status == "degraded" -%}
     (!) Performance degraded — investigate soon
     {%- endif %}
{% endfor %}

{% if len(tags) > 0 -%}
Tags: {{ tags | join(", ") }}
{%- endif %}

{% raw %}
Note: Template delimiters like {{ variable }} and {% tag %}
are part of Stash's tpl namespace — see docs for details.
{% endraw %}

--- End of Report ---
