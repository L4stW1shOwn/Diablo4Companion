# Design Document — Event Tracker Overlay

## Context

D4Companion est un overlay WPF pour Diablo IV qui affiche des informations directement sur la fenêtre du jeu via GameOverlay.Drawing. L'application utilise une architecture modulaire avec des services injectés via Dependency Injection (Microsoft.Extensions.DependencyInjection) et la communication entre composants via CommunityToolkit.Mvvm.Messaging (WeakReferenceMessenger).

L'utilisateur souhaite afficher un panneau d'événements en temps réel (World Boss, Helltide, Zone Event, Chest Respawn) en utilisant l'API publique `https://diablo4.life/api/trackers/list`, tout en réutilisant le système de rendu existant de l'overlay.

## Goals

1. Afficher un panneau overlay permanent avec les événements Diablo IV en cours / à venir.
2. Permettre à l'utilisateur de configurer quels événements afficher et où les positionner.
3. Envoyer des notifications visuelles (alertes) lorsqu'un événement approche (configurable).
4. Respecter l'architecture existante et ne pas alourdir `OverlayHandler` avec de la logique métier.

## Non-Goals

1. Édition ou création d'événements côté client.
2. Gestion de plusieurs sources d'API simultanées.
3. Internationalisation spécifique des noms d'événements (les noms viennent tels quels de l'API).

## Architecture

```
+------------------------------------------+
|  EventTrackerService (Singleton)         |
|  - Polling API toutes les 60s            |
|  - Parse & cache EventTrackerData        |
|  - Détection des alertes                 |
+------------------------------------------+
                    |
                    | lecture des données
                    v
+------------------------------------------+
|  OverlayHandler                          |
|  - DrawGraphicsEventTracker()            |
|  - DrawNotification() (alertes)          |
+------------------------------------------+
                    |
                    | gfx.DrawText / FillRectangle
                    v
+------------------------------------------+
|  GameOverlay.Drawing                     |
|  - Rendu final sur la fenêtre du jeu     |
+------------------------------------------+
```

## Components

### 1. EventTrackerService

**Namespace** : `D4Companion.Services`
**Interface** : `IEventTrackerService` (nouveau fichier dans `D4Companion.Interfaces`)

Responsabilités :
- Appeler l'API `https://diablo4.life/api/trackers/list` toutes les 60s via `System.Threading.Timer`.
- Parser la réponse JSON en `EventTrackerData` via `System.Text.Json`.
- Maintenir un cache interne et exposer une propriété `CurrentData`.
- Calculer les comptes à rebours (`TimeSpan`) à la demande.
- Détecter les alertes (un event approche dans les `X` minutes configurées) et notifier via `WeakReferenceMessenger`.
- Éviter le spam d'alerte en gardant en mémoire le dernier event alerté (par type + timestamp).

**Propriétés publiques** :
```csharp
EventTrackerData CurrentData { get; }
bool IsDataStale { get; }
```

**Méthodes publiques** :
```csharp
TimeSpan GetTimeRemaining(EventType eventType);
string GetDisplayText(EventType eventType); // ex: "Helltide: 12:34"
```

**Timer** : `System.Threading.Timer` avec période de 60s. Le premier tick est immédiat au démarrage du service.

### 2. Modèles de données

**Namespace** : `D4Companion.Entities`

```csharp
public class EventTrackerData
{
    public DateTimeOffset Helltide { get; set; }
    public WorldBossInfo WorldBoss { get; set; } = new();
    public DateTimeOffset ZoneEvent { get; set; }
    public DateTimeOffset ChestRespawn { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public bool IsStale { get; set; }
}

public class WorldBossInfo
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset Time { get; set; }
}

public enum EventType
{
    Helltide,
    WorldBoss,
    ZoneEvent,
    ChestRespawn
}
```

### 3. Settings

**Fichier** : `D4Companion.Entities/SettingsD4.cs`

Nouvelles propriétés à ajouter :

| Propriété | Type | Défaut | Description |
|---|---|---|---|
| `IsEventTrackerEnabled` | `bool` | `false` | Active/désactive le panneau |
| `IsEventTrackerHelltideEnabled` | `bool` | `true` | Affiche Helltide |
| `IsEventTrackerWorldBossEnabled` | `bool` | `true` | Affiche World Boss |
| `IsEventTrackerZoneEventEnabled` | `bool` | `true` | Affiche Zone Event |
| `IsEventTrackerChestRespawnEnabled` | `bool` | `true` | Affiche Chest Respawn |
| `EventTrackerPosX` | `int` | `900` | Position X du panneau (pixels absolus) |
| `EventTrackerPosY` | `int` | `10` | Position Y du panneau (pixels absolus) |
| `EventTrackerAlertMinutes` | `int` | `5` | Minutes avant l'alerte |
| `IsEventTrackerAlertsEnabled` | `bool` | `true` | Active les notifications d'alerte |

### 4. Intégration OverlayHandler

**Fichier** : `D4Companion.Services/OverlayHandler.cs`

Ajouts :
- Injection de `IEventTrackerService` dans le constructeur.
- Nouvelle méthode privée `DrawGraphicsEventTracker(DrawGraphicsEventArgs e)` appelée dans `DrawGraphics` si `IsEventTrackerEnabled` est vrai.
- La méthode lit `CurrentData` et les flags par type de event pour dessiner :
  - Un panneau (fond `backgroundTransparent`, bordure `border`) à la position configurée.
  - Une ligne par event activé avec le texte retourné par `IEventTrackerService.GetDisplayText(...)`.
  - Si `IsDataStale`, un indicateur `(!)` en rouge (`Colors.Red`) à côté du titre ou de chaque ligne.
- Réutilisation des ressources existantes : `_fonts["consolasBold"]`, `_brushes["text"]`, `_brushes["backgroundTransparent"]`, `_brushes["border"]`.

### 5. Notifications d'alerte

**Fichier** : `D4Companion.Services/OverlayHandler.cs` (mécanisme existant réutilisé)

- `EventTrackerService` détecte qu'un event approche dans les `EventTrackerAlertMinutes`.
- Il envoie un message `EventTrackerAlertMessage` (nouveau) via `WeakReferenceMessenger`.
- `OverlayHandler` s'abonne à ce message et appelle `SetNotificationText(...)` + active `_notificationVisible` + `_notificationTimer`.
- Le tracking du dernier event alerté évite de renvoyer la même alerte pour la même occurrence.

### 6. Messages MVVM

**Fichier** : `D4Companion.Messages/EventTrackerMessages.cs` (nouveau)

```csharp
public class EventTrackerAlertMessage : ValueChangedMessage<EventTrackerAlertMessageParams>
{
    public EventTrackerAlertMessage(EventTrackerAlertMessageParams value) : base(value) { }
}

public class EventTrackerAlertMessageParams
{
    public EventType EventType { get; set; }
    public string EventName { get; set; } = string.Empty;
    public TimeSpan TimeRemaining { get; set; }
}
```

## Data Flow

1. **Startup** (`App.xaml.cs`) : `EventTrackerService` est enregistré en singleton dans le `ServiceCollection`.
2. **Construction** : `EventTrackerService` démarre immédiatement le timer et effectue le premier appel API.
3. **Polling** : toutes les 60s, l'API est interrogée. En cas de succès, `CurrentData` est mis à jour et `IsStale` passe à `false`. En cas d'échec, l'erreur est logguée et `IsStale` passe à `true` si les données datent de plus de 5 minutes.
4. **Alerte** : à chaque mise à jour, `EventTrackerService` vérifie si un event approche dans le seuil configuré. Si oui et que cet event n'a pas encore été alerté, il publie `EventTrackerAlertMessage`.
5. **Render loop (60 FPS)** : `OverlayHandler.DrawGraphics` appelle `DrawGraphicsEventTracker(e)` si activé. Cette méthode accède en lecture à `IEventTrackerService.CurrentData` (pas de lock nécessaire si le service met à jour une référence atomique).
6. **Notification** : `OverlayHandler` reçoit `EventTrackerAlertMessage` et déclenche la notification temporaire via le mécanisme existant.

## Error Handling

| Scénario | Comportement |
|---|---|
| Échec réseau (API injoignable) | Log via `ILogger`. Conserver les anciennes données. Passer `IsStale` à `true` après 5 min. |
| Erreur de parsing JSON | Log via `ILogger`. Conserver les anciennes données. |
| Timestamp dans le passé / nul | Afficher `"Active"` au lieu d'un compte à rebours négatif. |
| API renvoie des données incomplètes | Parser les champs disponibles, ignorer les autres. |
| Overlay désactivé | `EventTrackerService` continue de fonctionner (pas de pause du timer), mais `OverlayHandler` ne dessine rien. |

## UI / Visual Design

### Panneau permanent

- **Position** : absolue en pixels (`EventTrackerPosX`, `EventTrackerPosY`).
- **Style** : identique aux panneaux existants (Paragon, Trading).
  - Fond : `_brushes["backgroundTransparent"]` (noir semi-transparent).
  - Bordure : `_brushes["border"]` (gris), stroke = 1.
  - Texte : `_fonts["consolasBold"]` (taille `OverlayFontSize`), couleur `_brushes["text"]`.
- **Contenu** : une ligne par event activé.
  - Format : `{EventLabel}: {TimeRemaining}`
  - Exemple : `Helltide: 12:34`, `World Boss (Avarice): 45:12`, `Zone Event: Active`
- **Données périmées** : préfixe `[!]` en rouge (`Colors.Red`) sur chaque ligne ou sur le titre du panneau.

### Notification d'alerte

- Réutilise le mécanisme existant : panneau temporaire centré en bas de l'item tooltip avec le texte de l'alerte.
- Format : `EventTracker: World Boss (Avarice) in 5 minutes!`
- Durée : 2000 ms (même timer que les autres notifications).

## Testing Plan

### Tests unitaires (`D4Companion.Tests`)

1. **`EventTrackerService.ParseResponse`**
   - Input : JSON de l'API réel.
   - Vérifier que tous les champs sont correctement parsés.
2. **`EventTrackerService.GetTimeRemaining`**
   - Vérifier le calcul pour un event futur, passé, et exactement maintenant.
3. **`EventTrackerService.AlertDetection`**
   - Vérifier qu'une alerte est déclenchée une seule fois par occurrence.
   - Vérifier qu'aucune alerte n'est envoyée si le seuil n'est pas atteint.
4. **`EventTrackerService.StaleData`**
   - Vérifier que `IsStale` devient `true` après 5 min sans mise à jour.

### Tests manuels

1. Activer/désactiver le panneau via Settings.
2. Activer/désactiver chaque type d'event individuellement.
3. Modifier la position X/Y et vérifier le repositionnement.
4. Vérifier l'alerte lorsqu'un event approche.
5. Déconnecter le réseau et vérifier le comportement stale.

## Risks & Mitigations

| Risque | Mitigation |
|---|---|
| L'API diablo4.life devient indisponible ou change de format | Le service continue avec les dernières données connues. Le parsing utilise `JsonSerializer` avec des champs optionnels pour éviter les crashs. |
| `OverlayHandler` devient trop lourd | Toute la logique métier est isolée dans `EventTrackerService`. `OverlayHandler` ne fait que lire et dessiner. |
| Spam d'alertes | Tracking du dernier event alerté par type + timestamp dans `EventTrackerService`. |
| Performance du timer à 60s | Utilisation de `System.Threading.Timer` (léger). Pas d'impact sur la boucle de rendu à 60 FPS. |

## Open Questions

- Aucune. Toutes les décisions ont été validées avec l'utilisateur.
