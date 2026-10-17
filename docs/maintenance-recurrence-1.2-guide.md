# Maintenance 1.2 — suggestions d'incidents récurrents (#109)

## Objectif et garanties

Nodalis peut proposer des **rapprochements hypothétiques** entre incidents de maintenance
déjà enregistrés. Il ne s'agit **pas de doublons confirmés**.
Le traitement est entièrement déterministe et local : pas de LLM,
pas d'accès au réseau, pas de télémétrie et pas de nouvelle base de données.

La détection ne fusionne, ne ferme ni ne supprime jamais un incident.
Un titre ou une description simplement ressemblants ne constituent pas une preuve.

## Où consulter les suggestions

1. Ouvrir **Historique de maintenance** depuis une fiche, le menu
   **Pilotage**, ou la palette de commandes (**Ctrl+P**).
2. Cocher **Afficher les suggestions d'incidents récurrents**. Le bouton
   **Examiner les suggestions** est alors visible. Le réglage est mémorisé
   localement dans les préférences de Nodalis.
3. Choisir le contexte **Projet** ou **Application** et cliquer sur
   **Examiner les suggestions**. La vue compare les incidents **côte à côte**
   : résumé, date, référence, symptômes, tags, source, score et justification.
4. Après lecture, choisir **Lier les incidents**, **Ignorer cette paire**
   ou **Fermer**. Les deux premières actions demandent une confirmation.
   Le choix Fermer ne modifie aucun document.

Décocher la préférence cache seulement les suggestions, **sans désactiver
la maintenance**, les interventions ou l'historique. Le réglage n'ajoute
aucune propriété aux documents de projet.

## Contrat d'heuristique (v1)

L'index reste celui de l'issue #108, dérivé des fichiers
**Fiche de Maintenance.md**. Seuls les incidents structurés ayant un
identifiant stable sont candidats. Une paire provient **de la même fiche
de maintenance**, jamais de deux projets différents. Ce garde-fou évite
de rapprocher des projets dont le vocabulaire technique est partagé.

Le moteur normalise le texte en minuscules sans accents, enlève la ponctuation,
élimine des mots-outils et calcule des intersections de tokens / similarités
de Jaccard. Le score est borné à 100, et **le seuil affiché est 70/100**.

| Indice indépendant | Score maximum | Règle |
|---|---:|---|
| Référence explicite identique | +50 | \`Référence\` ou \`Référence projet\`, longueur ≥ 3 |
| Symptômes similaires | +55 | Jaccard ≥ 0,65 et au moins 2 tokens communs |
| Symptômes très proches | +75 (au lieu de 55) | Jaccard ≥ 0,82 et 3 tokens communs |
| Tags identiques | +10 / +25 | Un ou au moins deux tags communs |
| Mots significatifs du résumé | +15 / +30 / +45 | Similarité ≥ 0,35 / 0,50 / 0,60 selon les tokens et les tags |

Deux indices indépendants sont nécessaires, sauf pour des symptômes
**très proches**. Une référence seule vaut 50 et ne déclenche rien.
Des résumés similaires seuls ne suffisent jamais. Les raisons et le score
sont affichés dans chaque suggestion ; l'affichage ne constitue jamais
une conclusion automatique.

Les paires sont classées par score, puis par chemin et identifiants stables
(maximum 200 suggestions affichées par consultation, pour éviter une UI massive).
La comparaison est recalculée à l'ouverture du panneau depuis le Markdown.

## Décisions humaines dans le Markdown

Seul le bloc de l'incident A reçoit éventuellement l'un des champs Markdown
optionnels suivants :

\`\`\`markdown
### Incident
- **Identifiant :** INC-…
- **Date :** 2026-10-17
- **Résumé :** Équipement indisponible
- **Récurrences liées :** INC-…
\`\`\`

ou :

\`\`\`markdown
- **Récurrences ignorées :** INC-…
\`\`\`

Les références utilisent les IDs stables existants. L'affichage reconnaît
ces décisions **dans les deux sens** : une paire déjà liée ou ignorée
n'est plus reproposée. Une nouvelle décision explicite inverse une ancienne
décision contradictoire. Le champ est librement consultable et modifiable
à la main. Aucun incident secondaire n'est modifié automatiquement.

**Sécurité :** Nodalis compare les empreintes SHA-256 des **deux blocs
examinés** avant l'écriture. Si l'un a changé, il refuse la décision et
demande une nouvelle consultation. Les changements dans les autres sections
sont conservés ; la persistance utilise \`TextDocumentSession\` et
\`AtomicFileWriter\` pour détecter les modifications concurrentes.

Les fiches incomplètes restent consultables dans l'historique, mais elles
ne sont pas automatiquement proposées au rapprochement : il faut d'abord
les structurer manuellement.

## Tests et recette

\`MaintenanceRecurrenceSmokeTests\` contient des cas positifs et négatifs :
une référence identique seule, un titre similaire seul, des symptômes concordants,
deux tags avec mots clés concordants, des incidents d'autres projets, et
des décisions liées/ignorées. **Le jeu de fixtures attend 2 vrais positifs,
0 faux positifs et 0 faux négatifs** ; le test échoue si ce résultat change.
Il vérifie aussi le refus de sauvegarde sur une version périmée et la
sérialisation du réglage de visibilité.

Les vérifications de focus, dark mode, scaling DPI et interactions
avec l'éditeur scindé nécessitent toujours une recette interactive Windows.

Voir [Historique et recherche de maintenance](maintenance-history-1.2-guide.md).
