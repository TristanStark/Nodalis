# Historique et recherche de maintenance — Nodalis 1.2 (#108)

La vue d'historique est une projection **en lecture seule** des fiches Markdown existantes.
Aucun index métier persistant ni nouvelle structure de projet n'est créée.

## Ouvrir l'historique

- Dans un projet disposant d'une **Fiche de Maintenance.md**, ouvrir cette fiche puis cliquer sur **Historique** à côté des commandes Nouvelle intervention / Modifier.
- Depuis un projet ou une application, utiliser **Pilotage > Historique de maintenance**.
- Dans la Command Palette (Ctrl+P), rechercher **Historique de maintenance**.

Le sélecteur **Contexte** propose *Projet* et, lorsque la sélection appartient à une application,
*Application*. Le scope Projet interroge la fiche placée à la racine du projet ; le scope
Application rassemble les fiches des projets et sous-projets de cette application.
Les fichiers sont découverts depuis l'arborescence de navigation, et lus localement
avec une concurrence limitée. Les erreurs de lecture sont signalées, sans masquer
silencieusement l'absence d'une fiche.

## Retrouver une résolution

La fenêtre montre les **incidents** d'un côté et les **interventions** de l'autre,
triés par date décroissante. Chaque entrée indique sa date, son résumé,
son projet et sa ligne source. Les interventions affichent aussi le **Problème** (résumé de l'incident lié,
si disponible) et le **Résultat** si ce champ est renseigné. La recherche
d'un problème retrouve également les interventions qui lui sont liées. Un double-clic, ou **Ouvrir la source**, place le
curseur sur le titre du bloc Markdown correspondant.

Sélectionner un incident puis cocher **Liées à l'incident sélectionné**
réduit la liste de droite aux interventions rattachées à son identifiant,
sans perdre la recherche en cours. Décocher revient à la chronologie entière.

Les filtres se combinent :
- champ de recherche libre : résumé, action, symptôme, cause, diagnostic,
  résultat, notes libres et autres propriétés ;
- dates **Du / Au** inclusives ;
- **Statut** et **Catégorie** sélectionnés parmi les valeurs connues ;
- **Tag** saisi, recherché parmi les tags séparés par des virgules.

Après chargement, le filtrage est effectué directement en mémoire, sans relire
les fichiers à chaque caractère tapé. Les listes activent la virtualisation
WPF afin de limiter le coût d'affichage pour les gros historiques.

## Notes libres et entrées incomplètes

Les fiches sans historique structuré restent valides et affichent zéro entrée.
Le lecteur n'exige pas que les anciens incidents aient tous un ID, une date
ou un résumé : chaque sous-titre **Incident** ou **Intervention** placé dans
la section **Historique de maintenance** reste navigable.

Les blocs incomplets affichent un avertissement (date absente, ID manquant,
champs dupliqués ou incident lié introuvable). **Une entrée sans date n'est
pas exclue par un filtre de période** : elle reste en fin de liste,
étiquetée *Date absente*, avec un avertissement visible dans la fenêtre.

Les exemples situés dans les fences Markdown ne deviennent pas des entrées et
leurs propriétés ne sont pas interprétées comme des métadonnées réelles.
Un bloc de texte libre sans titre Incident/Intervention demeure du Markdown libre,
sans migration automatique.

## Modifier depuis l'historique

Cliquer sur **Modifier** :
- une intervention entièrement structurée ouvre le même formulaire de modification
  que la commande de l'issue #107, ciblant son identifiant exact ;
- un incident ou une entrée incomplète ouvre la source Markdown pour une
  correction manuelle, sans réécrire ni tenter de normaliser le fichier.

Après la fermeture de la fenêtre, Nodalis recherche de nouveau l'identifiant
stable dans l'éditeur courant avant de placer le curseur, afin de ne pas pointer
sur une mauvaise ligne si des sections ont été insérées entre-temps.
Lorsqu'un bloc sans ID a changé de ligne ou de résumé, le navigateur affiche
un avertissement et n'invente pas de nouvelle position.

Toute édition est enregistrée par les mécanismes existants de sauvegarde
contrôlée : détection des modifications externes et enregistrement atomique.
L'historique lui-même n'enregistre pas les résultats de recherche et ne
modifie jamais un fichier source.

## Validation

La suite **MaintenanceHistorySmokeTests** vérifie le parsing de fiches mixtes,
les dates absentes, les liens vers des incidents, les champs partiels,
le filtrage combiné, l'absence de faux résultats dans les blocs de code,
et l'indexation de 1 500 interventions.

La vérification visuelle sous Windows (focus, DPI, lecteurs d'écran,
scroll, dark mode, dialogues) reste un contrôle distinct de la compilation,
des tests métier et du lancement de l'archive portable.

Voir également [Contrat Markdown 1.2](maintenance-markdown-1.2.md) et
[Guide de saisie et modification des interventions](maintenance-interventions-1.2-guide.md).
