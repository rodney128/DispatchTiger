using System;

namespace DispatchTiger.Views.Map
{
    /// <summary>
    /// Builds the static Google Maps HTML/JavaScript page that is loaded into the
    /// WebView2 control via NavigateToString. The JavaScript defines the marker,
    /// route, and zoom bridge functions (dispatchTigerSetMarkers / dispatchTigerZoomToJob)
    /// and opens a single read-only InfoWindow on marker click (no hover popups).
    /// </summary>
    internal static class MapHtmlBuilder
    {
        internal static string BuildMapHtml(string apiKey)
        {
            var mapId = Environment.GetEnvironmentVariable("GOOGLE_MAPS_MAP_ID") ?? "";
            bool useAdvancedMarker = !string.IsNullOrWhiteSpace(mapId);
            string mapIdLine      = useAdvancedMarker ? $"    mapId: '{MapMarkerBuilder.EscapeJs(mapId)}'," : "";
            string librariesParam = useAdvancedMarker ? "&libraries=marker" : "";
            string dtUseAdv       = useAdvancedMarker ? "true" : "false";

            return
$@"<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>
  html, body, #map {{ height: 100%; margin: 0; padding: 0; background: #1a1a1a; }}
</style>
</head>
<body>
<div id='map'></div>
<script>
window.dispatchTigerMap        = null;
window.dispatchTigerMarkerObjs = [];
window.dispatchTigerRouteLines = [];   // Polyline objects for the current route preview
window.dispatchTigerInfoWindow = null;

// Open the shared InfoWindow on a marker (click only — no hover popup).
window._dtOpenInfoWindow = function(marker, html) {{
  window.dispatchTigerInfoWindow.setContent(html);
  if (_dtUseAdvanced)
    window.dispatchTigerInfoWindow.open({{ map:window.dispatchTigerMap, anchor:marker }});
  else
    window.dispatchTigerInfoWindow.open(window.dispatchTigerMap, marker);
}};
var _dtUseAdvanced = {dtUseAdv};

async function initMap() {{
  const {{ Map }} = await google.maps.importLibrary('maps');
  window.dispatchTigerMap = new Map(document.getElementById('map'), {{
    center: {{ lat: 48.60, lng: -123.55 }},
    zoom: 9,
{mapIdLine}
    mapTypeId: 'roadmap',
    backgroundColor: '#1a1a1a',
  }});
  window.dispatchTigerInfoWindow = new google.maps.InfoWindow();
}}

window.dispatchTigerClearMarkers = function() {{
  if (window.dispatchTigerInfoWindow) {{
    window.dispatchTigerInfoWindow.close();
  }}
  window.dispatchTigerMarkerObjs.forEach(function(m) {{
    if (m.setMap) m.setMap(null);
    else if (typeof m.map !== 'undefined') m.map = null;
  }});
  window.dispatchTigerMarkerObjs = [];
}};

// Removes all current route preview polylines from the map.
window.dispatchTigerClearRouteLines = function() {{
  window.dispatchTigerRouteLines.forEach(function(l) {{ l.setMap(null); }});
  window.dispatchTigerRouteLines = [];
}};

// jsonStr    : JSON array of marker objects (unchanged from before)
// routeJson  : JSON object with optional truckToPickup / pickupToDelivery legs, or null
// fitBounds  : boolean — when true, auto-fit the viewport to the selected job's
//              pickup + delivery only (no fleet truck markers)
window.dispatchTigerSetMarkers = async function(jsonStr, routeJson, fitBounds) {{
  if (!window.dispatchTigerMap) return;
  window.dispatchTigerClearMarkers();
  window.dispatchTigerClearRouteLines();
  var markers; try {{ markers = JSON.parse(jsonStr); }} catch(e) {{ return; }}
  if (!markers || markers.length === 0) return;
  var map  = window.dispatchTigerMap;
  var info = window.dispatchTigerInfoWindow;
  if (_dtUseAdvanced) {{
    var {{ AdvancedMarkerElement }} = await google.maps.importLibrary('marker');
    markers.forEach(function(m) {{
      var pin;
      if (m.type === 'truck' && m.icon) {{
        pin = document.createElement('img');
        pin.src = m.icon;
        pin.width  = m.iconW || 48;
        pin.height = m.iconH || 32;
        pin.style.cssText = 'cursor:pointer;display:block;filter:drop-shadow(0 2px 3px rgba(0,0,0,.6))';
      }} else {{
        pin = document.createElement('div');
        pin.style.cssText = ['background:'+m.color,'color:#fff','border-radius:6px','padding:4px 8px','font:bold 11px/1.3 sans-serif','max-width:180px','white-space:pre-wrap','word-break:break-word','box-shadow:0 2px 6px rgba(0,0,0,.5)','cursor:pointer'].join(';');
        pin.textContent = m.label;
      }}
      var marker = new AdvancedMarkerElement({{ map:map, position:{{lat:m.lat,lng:m.lng}}, content:pin, title:m.label }});
      (function(md, mkr) {{
        mkr.addListener('click', function() {{
          var html = '<div style=\'font:13px sans-serif;white-space:pre-wrap;max-width:260px\'>' + md.label.replace(/\n/g,'<br>') + '</div>';
          window._dtOpenInfoWindow(mkr, html);
          if (md.type==='truck' && window.chrome && chrome.webview) chrome.webview.postMessage({{type:'truckClicked',truckId:md.truckId}});
        }});
      }})(m, marker);

      window.dispatchTigerMarkerObjs.push(marker);
    }});
  }} else {{
    markers.forEach(function(m) {{
      var iw = m.iconW || 48, ih = m.iconH || 32;
      var icon = m.icon
        ? {{ url:m.icon, scaledSize:new google.maps.Size(iw,ih), anchor:new google.maps.Point(iw/2,ih-4) }}
        : {{ path:google.maps.SymbolPath.CIRCLE, scale:10, fillColor:m.color, fillOpacity:1, strokeColor:'#ffffff', strokeWeight:2 }};
      var marker = new google.maps.Marker({{ map:map, position:{{lat:m.lat,lng:m.lng}}, icon:icon, title:m.label }});
      (function(md, mkr) {{
        mkr.addListener('click', function() {{
          var html = '<div style=\'font:13px sans-serif;white-space:pre-wrap;max-width:260px;padding:4px\'>' + md.label.replace(/\n/g,'<br>') + '</div>';
          window._dtOpenInfoWindow(mkr, html);
          if (md.type==='truck' && window.chrome && chrome.webview) chrome.webview.postMessage({{type:'truckClicked',truckId:md.truckId}});
        }});
      }})(m, marker);

      window.dispatchTigerMarkerObjs.push(marker);
    }});
  }}

  // Draw straight-line route preview polylines (no Directions API).
  // routeJson may be null (no job selected) or contain truckToPickup and/or pickupToDelivery legs.
  var routeBoundsPoints = [];
  // jobBoundsPoints holds ONLY the job's pickup + delivery so auto-fit frames the same
  // area as the explicit Zoom to Job button (truck positions never pull the viewport away).
  var jobBoundsPoints = [];
  if (routeJson) {{
    var route; try {{ route = JSON.parse(routeJson); }} catch(e) {{ route = null; }}
    if (route) {{
      // Dashed gold line: truck (or recommended truck) → pickup (deadhead / empty leg)
      if (route.truckToPickup) {{
        var leg = route.truckToPickup;
        var path = [{{lat:leg.fromLat,lng:leg.fromLng}},{{lat:leg.toLat,lng:leg.toLng}}];
        var opacity = leg.isStaged ? 0.85 : 0.55;   // staged truck line looks stronger
        var weight  = leg.isStaged ? 3   : 2;
        var dashedLine = new google.maps.Polyline({{
          map: map,
          path: path,
          strokeColor: '#F5A623',    // gold-orange: deadhead / empty leg
          strokeOpacity: 0,          // transparent stroke so dashes show through
          strokeWeight: 0,
          icons: [{{
            icon: {{ path:'M 0,-1 0,1', strokeOpacity: opacity, strokeWeight: weight, scale: 4 }},
            offset: '0', repeat: '14px'
          }}]
        }});
        window.dispatchTigerRouteLines.push(dashedLine);
        routeBoundsPoints.push({{lat:leg.fromLat,lng:leg.fromLng}});
        routeBoundsPoints.push({{lat:leg.toLat,  lng:leg.toLng  }});
        jobBoundsPoints.push({{lat:leg.toLat,   lng:leg.toLng   }}); // leg.to = pickup
      }}
      // Solid teal line: pickup → delivery (loaded leg)
      if (route.pickupToDelivery) {{
        var leg2 = route.pickupToDelivery;
        var solidLine = new google.maps.Polyline({{
          map: map,
          path: [{{lat:leg2.fromLat,lng:leg2.fromLng}},{{lat:leg2.toLat,lng:leg2.toLng}}],
          strokeColor: '#4DD0C4',    // teal: loaded / revenue leg
          strokeOpacity: 0.80,
          strokeWeight: 3
        }});
        window.dispatchTigerRouteLines.push(solidLine);
        routeBoundsPoints.push({{lat:leg2.fromLat,lng:leg2.fromLng}});
        routeBoundsPoints.push({{lat:leg2.toLat,  lng:leg2.toLng  }});
        jobBoundsPoints.push({{lat:leg2.fromLat, lng:leg2.fromLng }}); // pickup
        jobBoundsPoints.push({{lat:leg2.toLat,   lng:leg2.toLng   }}); // delivery
      }}
    }}
  }}

  if (fitBounds) {{
    // Auto-fit on job select: frame ONLY the job pickup/delivery (same as Zoom to Job).
    // Truck fleet positions are intentionally excluded so a faraway truck cannot push the job out of view.
    var fitPts = jobBoundsPoints.length > 0 ? jobBoundsPoints : routeBoundsPoints;
    if (fitPts.length === 1) {{
      map.setCenter(fitPts[0]); map.setZoom(12);
    }} else if (fitPts.length > 1) {{
      var b = new google.maps.LatLngBounds();
      fitPts.forEach(function(p){{ b.extend(p); }});
      map.fitBounds(b, {{top:80,right:80,bottom:80,left:80}});
    }}
  }}
}};

// Zoom the map to show only the selected job's pickup and delivery.
// jobBoundsJson : JSON array of {{lat,lng}} objects — pickup + delivery only.
// If the points are identical or too close, falls back to a reasonable zoom.
window.dispatchTigerZoomToJob = function(jobBoundsJson) {{
  if (!window.dispatchTigerMap) return;
  var pts; try {{ pts = JSON.parse(jobBoundsJson); }} catch(e) {{ return; }}
  if (!pts || pts.length === 0) return;
  if (pts.length === 1) {{
    window.dispatchTigerMap.setCenter(pts[0]); window.dispatchTigerMap.setZoom(13); return;
  }}
  var bounds = new google.maps.LatLngBounds();
  pts.forEach(function(p) {{ bounds.extend(new google.maps.LatLng(p.lat, p.lng)); }});
  // Guard against near-identical coords (< ~0.002 deg apart ≈ 200 m)
  var ne = bounds.getNorthEast(), sw = bounds.getSouthWest();
  if (Math.abs(ne.lat() - sw.lat()) < 0.002 && Math.abs(ne.lng() - sw.lng()) < 0.002) {{
    window.dispatchTigerMap.setCenter(bounds.getCenter()); window.dispatchTigerMap.setZoom(14); return;
  }}
  window.dispatchTigerMap.fitBounds(bounds, {{top:80,right:80,bottom:80,left:80}});
}};
</script>
<script src='https://maps.googleapis.com/maps/api/js?key={apiKey}&v=weekly{librariesParam}&callback=initMap' async defer></script>
</body>
</html>";
        }
    }
}
