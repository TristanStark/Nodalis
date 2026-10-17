# Contrat Markdown de maintenance — Nodalis 1.2 (#106)

Statut : **contrat de données implémenté sur develop**, sans interface de saisie à ce stade.
Source canonique : le texte de chaque fichier **Fiche de Maintenance.md** ; aucune base annexe.

## Modèle

Un **incident** représente un symptôme ou problème. Une **intervention** représente
une action effectuée, éventuellement en lien avec un incident déjà enregistré.
Plusieurs interventions peuvent pointer vers le même incident. Un incident peut
exister sans intervention, et une intervention peut être autonome.

Chaque enregistrement comporte uniquement trois champs obligatoires :
**Identifiant**, **Date**, **Résumé**. La date est une date civile ISO AAAA-MM-JJ,
sans heure ni fuseau. Le résumé tient sur une ligne. Tout autre champ est
facultatif ; les libellés sont écrits en français dans des listes Markdown.

Exemple à insérer dans le fichier existant (les identifiants sont indicatifs) :

~~~markdown
# Fiche de Maintenance

Mes anciennes notes et procédures restent ici, sans migration forcée.

## Historique de maintenance

### Incident
- **Identifiant :** INC-17f25cd459424482936ead593d7f614b
- **Date :** 2026-10-17
- **Résumé :** Erreurs HTTP 503 lors du démarrage
- **Symptômes :** Indisponibilité du service
- **Statut :** Ouvert
- **Tags :** exploitation, démarrage

### Intervention
- **Identifiant :** INT-615f7e50a4704abb92007d60e3ef07aa
- **Date :** 2026-10-18
- **Résumé :** Redémarrage du service
- **Incident :** INC-17f25cd459424482936ead593d7f614b
- **Diagnostic :** Service non initialisé
- **Action :** Redémarrage après correction de configuration
- **Résultat :** Service rétabli

## Procédures personnelles

Cette section libre est préservée à l'identique.
~~~

Les préfixes INC- et INT- sont suivis d'un GUID hexadécimal de 32 caractères ;
ils donnent une identité stable et permettent un lien lisible par un humain.
Un renommage ou déplacement du fichier **ne modifie pas les identifiants contenus
dans le document**. Les champs libres peuvent inclure cause, diagnostic, statut,
catégorie, tags, équipe, etc. Un lecteur doit ignorer les champs facultatifs qu'il
ne connaît pas, sans les supprimer du document.

Le titre de section **Historique de maintenance** (niveau 2) et les sous-titres
exacts **Incident** et **Intervention** (niveau 3) délimitent les enregistrements.
Le moteur ignore les exemples à l'intérieur des blocs de code Markdown.
Il n'utilise ni JSON caché ni commentaires propriétaires.

## Compatibilité et stratégie d'écriture

- **Lecture ancienne et nouvelle :** une fiche quelconque sans historique reste
  parfaitement lisible et éditable ; le parseur retourne simplement zéro entrée.
  Une nouvelle entrée crée la section d'historique seulement à la demande.
- **Round-trip non destructif :** l'ajout recopie le texte original sans le
  reformater. S'il existe une section d'historique, l'entrée est insérée avant
  la prochaine section de niveau 2, en gardant les notes libres et les fins de
  ligne LF/CRLF. Le lecteur ne normalise ni les blancs ni les propriétés.
- **Suppression :** si un bloc est supprimé manuellement, il disparaît de
  l'historique calculé. Un lien vers son ID devient orphelin et est signalé.
  Aucun objet supprimé n'est ressuscité par un cache.
- **Bloc invalide :** identifiant, date ou résumé manquant/invalide, clé dupliquée,
  ID dupliqué, cible de relation absente ou section historique en double :
  diagnostic explicite ; source Markdown intacte. Une insertion automatisée est
  refusée tant que les erreurs n'ont pas été corrigées manuellement.
- **Évolution :** les champs nouveaux doivent rester facultatifs et lisibles par
  les versions antérieures. Un changement incompatible de syntaxe ou de
  sémantique nécessite une version de schéma et une migration dédiée, conformément
  au contrat [1.0](data-contracts-v1.md). Ne jamais convertir les anciennes
  sections libres de manière implicite.
- **Enregistrement :** le codec est **pur** (texte en entrée, texte en sortie).
  Toute future interface (#107) doit relire le fichier au moment du clic
  Enregistrer, vérifier qu'il n'a pas changé depuis la prévisualisation et
  sauvegarder par fichier temporaire + remplacement atomique/backup contrôlé.
  Ne pas écraser une modification externe et ne pas supprimer la copie de secours
  avant confirmation de la sauvegarde.
- **Structure Ultra-simple :** toujours uniquement **Notes rapides.md** et
  **Fiche de Maintenance.md** à la création, plus le manifeste technique habituel.
  Le contrat 1.2 n'ajoute aucun fichier, dossier ou section métier automatique.

## API et limites

Dans le projet **Nodalis.Core** :

- **MaintenanceMarkdownCodec.NewId(kind)** crée les identifiants.
- **MaintenanceMarkdownCodec.Parse(markdown)** renvoie les entrées et diagnostics.
- **MaintenanceMarkdownCodec.Append(markdown, entry)** renvoie le document complet
  avec un enregistrement supplémentaire, ou échoue sans modifier le fichier.
- **MaintenanceRecord.IncidentId** référence un incident dans **la même fiche**.
  Les relations interdocuments relèvent de l'issue #111.
- Les résumés et valeurs de propriétés structurées sont actuellement limités
  à une ligne. Les notes riches et procédures multi-lignes restent possibles
  dans le Markdown libre. La saisie interactive est traitée dans l'issue #107.

Tests de non-régression : **MaintenanceMarkdownSmokeTests** dans la suite
**tests/Nodalis.SmokeTests** (document historique libre, clôtures de sections,
CRLF, identités, champs supplémentaires, références orphelines, duplications,
fichiers corrompus, insertion interdite en cas d'ambiguïté).
