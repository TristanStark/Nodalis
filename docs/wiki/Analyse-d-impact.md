# Analyse d'impact — guide utilisateur

> **Fonctionnalité en développement** sur `develop` (v1.3 / v1.4).
> La dernière version stable publiée reste v1.2.0 tant qu'aucun
> nouveau tag n'est annoncé.

L'analyse d'impact permet de répondre à **« Si ce document change, quels
autres documents pourraient être affectés et pourquoi ? »**.

## Démarrage en cinq actions

1. Ouvrir un document A, puis **Relations → + Ajouter**.
2. Choisir **dépend de** et sélectionner un document B.
3. Ouvrir B et cliquer **Impacts…** (ou **Ctrl+P → Analyser les impacts**).
4. Lire A comme impact **potentiel** de niveau 1, puis ouvrir le panneau
   **Pourquoi cet impact ?** pour lire la preuve.
5. Basculer sur **Graphe visuel** pour explorer les flèches, ou rester
   sur **Liste accessible** pour naviguer au clavier.

L'analyse de A ne remonte pas B : **A dépend de B** signifie que
changer **B** pourrait affecter **A**, pas l'inverse.

## Nouveautés graphiques (v1.4)

Le graphe WPF natif est limité à 90 nœuds visibles et possède un zoom
40–180 %, un recentrage et des ascenseurs. Le clic sur un nœud sélectionne
sa ligne dans la liste. Si le réseau est trop dense, la vue liste présente
l'ensemble des résultats calculés dans les limites configurées.

Les **traits continus** représentent les relations explicites ;
les **traits pointillés** représentent des suggestions contextuelles.

## Maintenance (v1.4)

Depuis l'**Historique de maintenance**, sélectionner un incident ou une
intervention et choisir **Analyser les impacts**. Nodalis demande quel
**document explicitement référencé** doit servir de point de départ.

L'onglet **Maintenance liée** présente les enregistrements de maintenance
référençant les documents concernés, avec les lignes sources.
Les liens de maintenance apportent des preuves d'association,
**jamais une causalité certaine**.

## Filtres et sûreté

Les filtres permettent de limiter les résultats par texte, type de preuve,
niveau et profondeur. Une analyse transversale entre projets et applications
demande de cocher **Inclure les autres projets…**.

Le moteur n'altère jamais le Markdown et ne nécessite ni Internet ni IA.
Les liens cassés sont signalés ; un lien simple `[[B]]` n'est pas une
dépendance métier.

La documentation complète, avec matrice des types de relation, limites,
options et dépannage est disponible dans :
[docs/impact-user-guide-1.4.md](https://github.com/TristanStark/Nodalis/blob/develop/docs/impact-user-guide-1.4.md).

Voir aussi [[Editeur-et-liens]] et [[Roadmap]].
